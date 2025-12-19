using Amazon.Runtime.Internal;
using Amazon.S3;
using CsvHelper;
using EmailCampaign.Models;
using System.Globalization;

namespace EmailCampaign.Services;

public class CampaignService : ICampaignService
{
    private readonly IAmazonS3 _s3;
    private readonly IEmailSenderService _emailService;
    private readonly ITemplateService _templateService;

    public CampaignService(IAmazonS3 s3, IEmailSenderService emailService, ITemplateService templateService)
    {
        _s3 = s3;
        _emailService = emailService;
        _templateService = templateService;
    }
   public bool IsValidEmail(string email)
    {
        if (string.IsNullOrWhiteSpace(email))
            return false;

        try
        {
            var addr = new System.Net.Mail.MailAddress(email);
            return addr.Address == email;
        }
        catch
        {
            return false;
        }
    }

    public async Task<object> StartCampaignAsync(CampaignRequest request, CampaignModelRequest model)
    {
        int totalSent = 0;
        int batchSize = 14;  // Adjust based on SES or provider limit
        var batch = new List<string>(batchSize);

        // Get the CSV object from S3
        var obj = await _s3.GetObjectAsync(request.Bucket, request.Key);
        using var reader = new StreamReader(obj.ResponseStream);
        using var csv = new CsvReader(reader, CultureInfo.InvariantCulture);

        csv.Read();
        csv.ReadHeader();

        while (csv.Read())
        {
            var email = csv.GetField("Email");
            var active = csv.GetField<bool>("IsActive");

            if (!IsValidEmail(email) || !active)
                continue;

            batch.Add(email);

            if (batch.Count >= batchSize)
            {
                await SendBatchAsync(batch, request.Campaign, model);
                totalSent += batch.Count;
                batch.Clear();

                // Optional delay to respect rate limits
                await Task.Delay(1000);
            }
        }

        // Send remaining emails
        if (batch.Count > 0)
        {
            await SendBatchAsync(batch, request.Campaign, model);
            totalSent += batch.Count;
        }

        return new
        {
            message = "Campaign started",
            total = totalSent
        };
    }

    // Helper method to send emails concurrently in a batch
    private async Task SendBatchAsync(List<string> emails, string campaign, CampaignModelRequest model)
    {
        var templateHtml = await _templateService.GetTemplateAsync(model.TemplateName);

        await Task.WhenAll(emails.Select(async email =>
        {
            var unsubscribeToken = Guid.NewGuid().ToString("N");
            await _emailService.SendCampaignEmailAsync(email, campaign, unsubscribeToken, templateHtml, model);
        }));
    }

}
