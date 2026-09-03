using Blocks.Genesis;
using Mail.DomainService.Entities;
using Mail.DomainService.Utilities;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Graph.Models;
using System.Net;
using System.Net.Mail;

namespace Mail.DomainService.Mails
{
    public interface INetSmtpClient : IDisposable
    {
        Task SendMailAsync(MailMessage message);
    }

    internal sealed class NetSmtpClientAdapter : INetSmtpClient
    {
        private readonly System.Net.Mail.SmtpClient _client;

        public NetSmtpClientAdapter(System.Net.Mail.SmtpClient client)
        {
            _client = client;
        }

        public Task SendMailAsync(MailMessage message) => _client.SendMailAsync(message);
        public void Dispose() => _client.Dispose();
    }

    public class MicrosoftSmtpClient : ISmtpClient
    {
        private readonly ILogger<MicrosoftSmtpClient> _logger;
        private readonly IConfiguration _configuration;

        public MicrosoftSmtpClient(ILogger<MicrosoftSmtpClient> logger, IConfiguration configuration)
        {
            _logger = logger;
            _configuration = configuration;
        }

        protected virtual INetSmtpClient CreateSmtpClient(MailServerConfiguration config)
        {
            var client = new System.Net.Mail.SmtpClient
            {
                Port = config.Port,
                DeliveryMethod = SmtpDeliveryMethod.Network,
                EnableSsl = true,
                Host = config.Host,
                UseDefaultCredentials = false,
                Credentials = new NetworkCredential(
                    config.SenderUserName,
                    config.AccountPassword),
            };

            return new NetSmtpClientAdapter(client);
        }

        public async Task<bool> SendAsync(MailToBeSent mailToBeSent, MailBody mailBody)
        {
            using (var client = CreateSmtpClient(mailToBeSent.MailServerConfiguration))
            {
                using (MailMessage mail = new MailMessage
                {
                    IsBodyHtml = true,
                    Subject = mailBody.Subject,
                    Body = mailBody.Body
                })
                {
                    bool added = AddMessageFrom(mailToBeSent, mail);

                    if (!added) return false;

                    foreach (var s in mailToBeSent.To)
                    {
                        mail.To.Add(new MailAddress(s));
                    }

                    if (mailToBeSent.Cc != null)
                    {
                        foreach (var s in mailToBeSent.Cc)
                        {
                            mail.CC.Add(new MailAddress(s));
                        }
                    }

                    if (mailToBeSent.Bcc != null)
                    {
                        foreach (var s in mailToBeSent.Bcc)
                        {
                            mail.Bcc.Add(new MailAddress(s));
                        }
                    }

                    if (mailToBeSent.ReplyTo != null)
                    {
                        foreach (var reply in mailToBeSent.ReplyTo)
                        {
                            mail.ReplyToList.Add(reply);
                        }
                    }

                    // MailMessage.Dispose() disposes its attachments, and the enclosing using block
                    // owns the message - so these streams must not be wrapped in a using of their own.
                    foreach (var attachment in mailBody.Attachments)
                    {
                        mail.Attachments.Add(new System.Net.Mail.Attachment(
                            new MemoryStream(attachment.Content),
                            attachment.FileName,
                            attachment.ContentType));
                    }

                    try
                    {
                        _logger.LogInformation(
                            "SMTP (System.Net.Mail): sending itemId={ItemId} via {Host}:{Port} with {AttachmentCount} attachment(s)",
                            mailToBeSent.ItemId,
                            mailToBeSent.MailServerConfiguration.Host,
                            mailToBeSent.MailServerConfiguration.Port,
                            mailBody.Attachments.Count);

                        if (mailToBeSent.MailServerConfiguration.IsEnableSnsConfiguration)
                        {
                            mail.Headers.Add("X-SES-CONFIGURATION-SET", _configuration["SnsConfigurationName"]);
                            mail.Headers.Add("X-Tenant-Id", BlocksContext.GetContext()?.TenantId);

                            // System.Net.Mail rejects CR/LF inside a header value, and every HTML
                            // template has newlines - so unfolded this threw for the whole message,
                            // was swallowed below, and surfaced only as a bare FAILED log.
                            mail.Headers.Add("X-Mail-Body", SingleLine(mailBody.Body));
                        }

                        await client.SendMailAsync(mail);
                        return true;
                    }
                    catch (Exception e)
                    {
                        _logger.LogError(
                            e,
                            "SMTP (System.Net.Mail) FAILED for itemId={ItemId} host={Host}:{Port}: {ExceptionType}: {Message}",
                            mailToBeSent.ItemId,
                            mailToBeSent.MailServerConfiguration.Host,
                            mailToBeSent.MailServerConfiguration.Port,
                            e.GetType().Name,
                            e.Message);
                    }
                }
                return false;
            }
        }

        private static string SingleLine(string? value)
        {
            return string.IsNullOrEmpty(value)
                ? string.Empty
                : value.Replace("\r", " ", StringComparison.Ordinal).Replace("\n", " ", StringComparison.Ordinal);
        }

        private bool AddMessageFrom(MailToBeSent mailToBeSent, MailMessage message)
        {

            _logger.LogInformation("++Adding message from static configs");

            message.From = new MailAddress(
                address: mailToBeSent.MailServerConfiguration.SenderAddress,
                displayName: mailToBeSent.MailServerConfiguration.SenderName);
            return true;

        }
    }
}
