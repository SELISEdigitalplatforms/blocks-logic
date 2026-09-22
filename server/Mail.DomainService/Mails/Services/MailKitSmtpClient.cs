using Blocks.Genesis;
using Mail.DomainService.Entities;
using Mail.DomainService.Utilities;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MailKit.Security;
using MimeKit;

namespace Mail.DomainService.Mails
{
    /// <summary>
    /// The seam over MailKit's SMTP client, so a sender can be tested without a socket.
    /// </summary>
    /// <remarks>
    /// The password members are the original ones and keep their exact behaviour. The two added
    /// below exist because neither original can express an OAuth session: <c>bool useSsl</c>
    /// cannot ask for STARTTLS, and a username/password pair cannot carry a SASL mechanism.
    /// </remarks>
    public interface IMailKitSmtpClient : IDisposable
    {
        Task ConnectAsync(string host, int port, bool useSsl);
        Task AuthenticateAsync(string userName, string password);

        /// <summary>Connects with an explicit socket option, for transports that require STARTTLS.</summary>
        Task ConnectAsync(string host, int port, SecureSocketOptions socketOptions);

        /// <summary>Authenticates with a SASL mechanism, for XOAUTH2.</summary>
        Task AuthenticateAsync(SaslMechanism mechanism);

        Task SendAsync(MimeMessage message);
        Task DisconnectAsync(bool quit);
    }

    internal sealed class MailKitSmtpClientAdapter : IMailKitSmtpClient
    {
        private readonly MailKit.Net.Smtp.SmtpClient _client = new();

        public Task ConnectAsync(string host, int port, bool useSsl) => _client.ConnectAsync(host, port, useSsl);
        public Task AuthenticateAsync(string userName, string password) => _client.AuthenticateAsync(userName, password);
        public Task ConnectAsync(string host, int port, SecureSocketOptions socketOptions) => _client.ConnectAsync(host, port, socketOptions);
        public Task AuthenticateAsync(SaslMechanism mechanism) => _client.AuthenticateAsync(mechanism);
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

        public async Task<bool> SendAsync(MailToBeSent mailToBeSent, MailBody mailBody)
        {
            var message = MailMessageComposer.Compose(mailToBeSent, mailBody);

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
                _logger.LogInformation("Sns configuration enabled: {IsEnableSnsConfiguration}", mailToBeSent.MailServerConfiguration.SendsSnsHeaders());
                if (mailToBeSent.MailServerConfiguration.SendsSnsHeaders())
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
