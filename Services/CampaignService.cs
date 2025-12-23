using Amazon.S3;
using Amazon.SimpleEmailV2;
using Amazon.SimpleEmailV2.Model;
using CsvHelper;
using DnsClient;
using EmailCampaign.Models;
using System.Collections.Concurrent;
using System.Globalization;
using System.Net.Mail;

namespace EmailCampaign.Services
{
    public class CampaignService : ICampaignService
    {
        private readonly IAmazonS3 _s3;
        private readonly IEmailSenderService _emailService;
        private readonly ITemplateService _templateService;
        private readonly IAmazonSimpleEmailServiceV2 _ses;

        private static readonly LookupClient _dnsClient = new();

        private readonly ConcurrentDictionary<string, bool> _domainMxCache = new();
        private readonly ConcurrentDictionary<string, bool> _suppressionCache = new();
        private readonly ConcurrentBag<string> _invalidLogs = new();

        private readonly SemaphoreSlim _mxThrottle = new(5);
        private readonly SemaphoreSlim _suppressionThrottle = new(1);

        private static readonly string InvalidLogFile =
            Path.Combine(Path.GetTempPath(), "InvalidEmails.txt");

        public CampaignService(
            IAmazonS3 s3,
            IEmailSenderService emailService,
            IAmazonSimpleEmailServiceV2 ses,
            ITemplateService templateService)
        {
            _s3 = s3;
            _emailService = emailService;
            _ses = ses;
            _templateService = templateService;
        }

        // ------------------ SYNTAX CHECK ------------------
        private bool IsValidSyntax(string email)
        {
            return MailAddress.TryCreate(email, out _);
        }

        // ------------------ MX CHECK (CACHED) ------------------
        private async Task<bool> HasMxRecordAsync(string domain)
        {
            if (_domainMxCache.TryGetValue(domain, out var cached))
                return cached;

            await _mxThrottle.WaitAsync();
            try
            {
                if (_domainMxCache.TryGetValue(domain, out cached))
                    return cached;

                var mxResult = await _dnsClient.QueryAsync(domain, QueryType.MX);
                bool valid = mxResult.Answers.MxRecords().Any();

                if (!valid)
                {
                    var aResult = await _dnsClient.QueryAsync(domain, QueryType.A);
                    valid = aResult.Answers.ARecords().Any();
                }

                _domainMxCache[domain] = valid;
                return valid;
            }
            catch
            {
                _domainMxCache[domain] = false;
                return false;
            }
            finally
            {
                _mxThrottle.Release();
            }
        }

        // ------------------ SES SUPPRESSION CHECK ------------------
        private async Task<bool> IsOnSesSuppressionListAsync(string email)
        {
            if (_suppressionCache.TryGetValue(email, out var cached))
                return cached;

            await _suppressionThrottle.WaitAsync();
            try
            {
                if (_suppressionCache.TryGetValue(email, out cached))
                    return cached;

                try
                {
                    var response = await _ses.GetSuppressedDestinationAsync(
                        new GetSuppressedDestinationRequest
                        {
                            EmailAddress = email
                        });

                    _suppressionCache[email] = response?.SuppressedDestination != null;
                }
                catch (AmazonSimpleEmailServiceV2Exception ex)
                    when (ex.ErrorCode == "NotFoundException")
                {
                    _suppressionCache[email] = false;
                }
                catch
                {
                    _suppressionCache[email] = false; // fail-safe
                }

                return _suppressionCache[email];
            }
            finally
            {
                _suppressionThrottle.Release();
            }
        }

        // ------------------ FULL EMAIL VALIDATION ------------------
        private async Task<bool> FullEmailCheckAsync(string email)
        {
            try
            {
                if (!IsValidSyntax(email))
                {
                    LogInvalid(email, "Invalid syntax");
                    return false;
                }

                if (await IsOnSesSuppressionListAsync(email))
                {
                    LogInvalid(email, "SES suppressed");
                    return false;
                }

                var domain = email.Split('@')[1];
                if (!await HasMxRecordAsync(domain))
                {
                    LogInvalid(email, "No MX record");
                    return false;
                }

                return true;
            }
            catch
            {
                LogInvalid(email, "Unexpected error");
                return false;
            }
        }

        private void LogInvalid(string email, string reason)
        {
            _invalidLogs.Add($"{email} ==> {reason}");
        }

        private async Task FlushInvalidLogsAsync()
        {
            if (_invalidLogs.Any())
                await File.AppendAllLinesAsync(InvalidLogFile, _invalidLogs);
        }

        // ------------------ START CAMPAIGN ------------------
        public async Task<object> StartCampaignAsync(
            CampaignRequest request,
            CampaignModelRequest model)
        {
            try
            {
                var s3Obj = await _s3.GetObjectAsync(request.Bucket, request.Key);

                List<string> emails = new();

                using (var reader = new StreamReader(s3Obj.ResponseStream))
                using (var csv = new CsvReader(reader, CultureInfo.InvariantCulture))
                {
                    csv.Read();
                    csv.ReadHeader();

                    while (csv.Read())
                    {
                        try
                        {
                            if (!csv.GetField<bool>("IsActive"))
                                continue;

                            emails.Add(csv.GetField("Email"));
                        }
                        catch { }
                    }
                }

                var validEmails = new ConcurrentBag<string>();

                await Parallel.ForEachAsync(
                    emails,
                    new ParallelOptions { MaxDegreeOfParallelism = 20 },
                    async (email, _) =>
                    {
                        if (await FullEmailCheckAsync(email))
                            validEmails.Add(email);
                    });

                await FlushInvalidLogsAsync();

                int sent = await SendCampaignEmailsAsync(
                    validEmails.ToList(),
                    request.Campaign,
                    model);

                return new
                {
                    message = "Campaign Completed",
                    total = emails.Count,
                    valid = validEmails.Count,
                    invalid = emails.Count - validEmails.Count,
                    
                };
            }
            catch (Exception ex)
            {
                return new
                {
                    message = "Campaign Failed",
                    error = ex.Message
                };
            }
        }

        // ------------------ EMAIL SENDING ------------------
        private async Task<int> SendCampaignEmailsAsync(
            List<string> emails,
            string campaign,
            CampaignModelRequest model)
        {
            if (!emails.Any())
                return 0;

            var template = await _templateService.GetTemplateAsync(model.TemplateName);

            int sent = 0;
            int batchSize = 10;
            int maxParallel = 3;

            SemaphoreSlim throttler = new(maxParallel);

            foreach (var batch in emails.Chunk(batchSize))
            {
                await Task.WhenAll(batch.Select(async email =>
                {
                    await throttler.WaitAsync();
                    try
                    {
                        await SendWithRetryAsync(async () =>
                        {
                            var token = Guid.NewGuid().ToString("N");
                            await _emailService.SendCampaignEmailAsync(
                                email,
                                campaign,
                                token,
                                template,
                                model);
                        });

                        Interlocked.Increment(ref sent);
                    }
                    finally
                    {
                        throttler.Release();
                    }
                }));

                await Task.Delay(2000); // SES safe delay
            }

            return sent;
        }

        private async Task SendWithRetryAsync(Func<Task> sendAction)
        {
            int retry = 0;

            while (true)
            {
                try
                {
                    await sendAction();
                    return;
                }
                catch (AmazonSimpleEmailServiceV2Exception ex)
                    when (ex.ErrorCode == "MaxSendRateExceeded")
                {
                    retry++;
                    if (retry > 5)
                        throw;

                    await Task.Delay(500 * retry);
                }
            }
        }
    }
}
