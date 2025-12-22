using Amazon.SimpleEmailV2;
using Amazon.SimpleEmailV2.Model;
using EmailCampaign.Models;
using System.Security.Cryptography;
using System.Text;

namespace EmailCampaign.Services;

public interface IEmailSenderService
{
    Task SendCampaignEmailAsync(string email, string campaign, string unsubscribeToken, string htmlContent, CampaignModelRequest model);
}

public class EmailSenderService : IEmailSenderService
{
    private readonly IAmazonSimpleEmailServiceV2 _ses;

    public EmailSenderService(IAmazonSimpleEmailServiceV2 ses)
    {
        _ses = ses;
    }

    public static class CryptoHelper
    {
        private static readonly string key = "A7F92B3C4D5E6F78"; // 16 chars = 128-bit
        private static readonly string iv = "1234567890ABCDEF"; // 16 chars

        public static string Encrypt(string plainText)
        {
            using (Aes aes = Aes.Create())
            {
                aes.Key = Encoding.UTF8.GetBytes(key);
                aes.IV = Encoding.UTF8.GetBytes(iv);

                using (MemoryStream ms = new MemoryStream())
                using (ICryptoTransform encryptor = aes.CreateEncryptor())
                using (CryptoStream cryptoStream = new CryptoStream(ms, encryptor, CryptoStreamMode.Write))
                {
                    byte[] plainBytes = Encoding.UTF8.GetBytes(plainText);
                    cryptoStream.Write(plainBytes, 0, plainBytes.Length);
                    cryptoStream.Close();

                    return Convert.ToBase64String(ms.ToArray())
                        .Replace("+", "-")
                        .Replace("/", "_")
                        .Replace("=", "");
                }
            }
        }
    }

    public async Task SendCampaignEmailAsync(
        string email,
        string campaign,
        string unsubscribeToken,
        string htmlContent,
        CampaignModelRequest model)
    {
        string encryptedEmail = CryptoHelper.Encrypt(email);

        string unsubscribeUrl = $"https://www.paperlessinspectors.com/unsubscribe/Unsubscribe.aspx?token={encryptedEmail}";
        string requestUrl = $"https://www.inspectiondepot.com/request-a-service?domain=1&emailid={encryptedEmail}";

        var finalHtml = htmlContent.Replace("###unsubscribe###", unsubscribeUrl);
        finalHtml = htmlContent.Replace("###requestUrl###", requestUrl);

        var request = new SendEmailRequest
        {
            FromEmailAddress = model.FromEmail,
            Destination = new Destination
            {
                ToAddresses = new List<string> { email }
            },
            Content = new EmailContent
            {
                Simple = new Message
                {
                    Subject = new Content { Data = model.Subject },
                    Body = new Body
                    {
                        Html = new Content
                        {
                            Data = finalHtml
                        }

                    }
                }
            },
            ConfigurationSetName = "christmas",
            EmailTags = new List<MessageTag>
            {
                new MessageTag { Name = "campaign", Value = campaign }
            }
        };

        await _ses.SendEmailAsync(request);
    }
}
