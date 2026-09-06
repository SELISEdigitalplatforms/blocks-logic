using Blocks.Genesis;
using Mail.DomainService.Entities;
using Mail.DomainService.Utilities;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MimeKit;

namespace Mail.DomainService.Mails
{
    public interface IMailKitSmtpClient : IDisposable
    {
        Task ConnectAsync(string host, int port, bool useSsl);
        Task AuthenticateAsync(string userName, string password);
        Task SendAsync(MimeMessage message);
        Task DisconnectAsync(bool quit);
    }

    internal sealed class MailKitSmtpClientAdapter : IMailKitSmtpClient
    {
        private readonly MailKit.Net.Smtp.SmtpClient _client = new();

        public Task ConnectAsync(string host, int port, bool useSsl) => _client.ConnectAsync(host, port, useSsl);
        public Task AuthenticateAsync(string userName, string password) => _client.AuthenticateAsync(userName, password);
        public Task SendAsync(MimeMessage message) => _client.SendAsync(message);
        public Task DisconnectAsync(bool quit) => _client.DisconnectAsync(quit);
        public void Dispose() => _client.Dispose();
    }

    public class MailKitSmtpClient : ISmtpClient
    {
        private readonly ILogger<MailKitSmtpClient> _logger;
        private readonly IConfiguration _configuration;

        public MailKitSmtpClient(ILogger<MailKitSmtpClient> logger,
                                IConfiguration configuration)
        {
            _logger = logger;
            _configuration = configuration;
        }

        protected virtual IMailKitSmtpClient CreateSmtpClient()
        {
            return new MailKitSmtpClientAdapter();
        }

        private static ContentType ParseContentType(string? contentType)
        {
            if (!string.IsNullOrWhiteSpace(contentType) && ContentType.TryParse(contentType, out var parsed))
            {
                return parsed;
            }

            return new ContentType("application", "octet-stream");
        }

        public async Task<bool> SendAsync(MailToBeSent mailToBeSent, MailBody mailBody)
        {
            var message = new MimeMessage
            {
                Subject = mailBody.Subject,
            };

            var bodyBuilder = new BodyBuilder
            {
                HtmlBody = mailBody.Body
            };

            foreach (var attachment in mailBody.Attachments)
            {
                bodyBuilder.Attachments.Add(attachment.FileName, attachment.Content, ParseContentType(attachment.ContentType));
            }

            // ToMessageBody() snapshots the builder, so this has to stay below the attachment loop.
            message.Body = bodyBuilder.ToMessageBody();
            message.From.Add(new MailboxAddress(mailToBeSent.MailServerConfiguration.SenderName, mailToBeSent.MailServerConfiguration.SenderAddress));

            foreach (var recipient in mailToBeSent.To)
            {
                message.To.Add(MailboxAddress.Parse(recipient));
            }

            if (mailToBeSent.Cc != null)
            {
                foreach (var cc in mailToBeSent.Cc)
                {
                    message.Cc.Add(MailboxAddress.Parse(cc));
                }
            }

            if (mailToBeSent.Bcc != null)
            {
                foreach (var bcc in mailToBeSent.Bcc)
                {
                    message.Bcc.Add(MailboxAddress.Parse(bcc));
                }
            }

            if (mailToBeSent.ReplyTo != null)
            {
                foreach (var replyTo in mailToBeSent.ReplyTo)
                {
                    message.ReplyTo.Add(MailboxAddress.Parse(replyTo));
                }
            }

            try
            {
                using var client = CreateSmtpClient();

                _logger.LogInformation(
                    "SMTP (MailKit): connecting to {Host}:{Port} ssl={EnableSsl} for itemId={ItemId} with {AttachmentCount} attachment(s)",
                    mailToBeSent.MailServerConfiguration.Host,
                    mailToBeSent.MailServerConfiguration.Port,
                    mailToBeSent.MailServerConfiguration.EnableSSL,
                    mailToBeSent.ItemId,
                    mailBody.Attachments.Count);

                await client.ConnectAsync(mailToBeSent.MailServerConfiguration.Host,
                    mailToBeSent.MailServerConfiguration.Port, mailToBeSent.MailServerConfiguration.EnableSSL);

                await client.AuthenticateAsync(mailToBeSent.MailServerConfiguration.SenderUserName,
                    mailToBeSent.MailServerConfiguration.AccountPassword);

                _logger.LogInformation("SMTP (MailKit): authenticated as {UserName} for itemId={ItemId}", mailToBeSent.MailServerConfiguration.SenderUserName, mailToBeSent.ItemId);
                _logger.LogInformation("Sns configuration enabled: {IsEnableSnsConfiguration}", mailToBeSent.MailServerConfiguration.IsEnableSnsConfiguration);
                if (mailToBeSent.MailServerConfiguration.IsEnableSnsConfiguration)
                {

                    message.Headers.Add("X-SES-CONFIGURATION-SET", _configuration["SnsConfigurationName"]);
                    message.Headers.Add("X-Tenant-Id", BlocksContext.GetContext()?.TenantId);
                    message.Headers.Add("X-Mail-Body", mailBody.Body);

                }

                await client.SendAsync(message);
                await client.DisconnectAsync(true);

                return true;
            }
            catch (Exception e)
            {
                // The exception type is the useful bit here: authentication, TLS, size limit and
                // "recipient rejected" all surface as different MailKit exceptions.
                _logger.LogError(
                    e,
                    "SMTP (MailKit) FAILED for itemId={ItemId} host={Host}:{Port}: {ExceptionType}: {Message}",
                    mailToBeSent.ItemId,
                    mailToBeSent.MailServerConfiguration.Host,
                    mailToBeSent.MailServerConfiguration.Port,
                    e.GetType().Name,
                    e.Message);
                return false;
            }
        }
    }
}
