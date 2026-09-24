using Blocks.Genesis;
using Mail.DomainService.Entities;
using Mail.DomainService.Mails.Strategies;
using Mail.DomainService.Shared.Enums;
using Microsoft.Extensions.Logging;

namespace Mail.DomainService.Mails.Office365
{
    /// <summary>
    /// The outbound strategy for Office 365: validates the record, then picks the transport by
    /// authentication type.
    /// </summary>
    /// <remarks>
    /// OAuth client credentials go through Microsoft Graph (<see cref="Office365GraphMailSender"/>);
    /// a mailbox password goes through SMTP (<see cref="Office365SmtpClient"/>), because Graph has no
    /// password mode. The record shape is the same for both and unchanged, so an existing
    /// configuration moves to Graph without being edited. It never enters the legacy
    /// <c>SmtpClient</c> branch, whatever the record stores in that field.
    /// </remarks>
    public sealed class Office365MailSender : IOutboundMailSender
    {
        private readonly Office365GraphMailSender _graph;
        private readonly Office365SmtpClient _smtp;
        private readonly ILogger<Office365MailSender> _logger;

        public Office365MailSender(
            Office365GraphMailSender graph,
            Office365SmtpClient smtp,
            ILogger<Office365MailSender> logger)
        {
            _graph = graph;
            _smtp = smtp;
            _logger = logger;
        }

        public MailServiceProvider Provider => MailServiceProvider.Office365Smtp;

        /// <summary>
        /// True: every failure path on both transports emits exactly one classified Office 365
        /// error, so the orchestrator's generic summary would be a second, less useful account of
        /// the same send.
        /// </summary>
        public bool EmitsOwnFailureDiagnostic => true;

        public Task<bool> SendAsync(MailToBeSent mailToBeSent, MailBody mailBody)
        {
            var configuration = mailToBeSent.MailServerConfiguration;
            var blocksTenantId = BlocksContext.GetContext()?.TenantId;

            var invalid = Office365ConfigurationContract.Validate(configuration, blocksTenantId);
            if (invalid is not null)
            {
                // Before any secret or network access, which is the point: a bad record costs one
                // log line, not a vault read and a request.
                var password = configuration.AuthenticationType == MailAuthenticationType.Password;
                Office365FailureLog.Write(
                    _logger,
                    Office365FailureCode.ConfigInvalid,
                    mailToBeSent,
                    password ? Office365ConfigurationContract.SmtpHost : Office365GraphMailSender.GraphHost,
                    password ? Office365ConfigurationContract.SmtpPort : Office365GraphMailSender.GraphPort,
                    exception: null,
                    reason: invalid);
                return Task.FromResult(false);
            }

            return configuration.AuthenticationType == MailAuthenticationType.Password
                ? _smtp.SendAsync(mailToBeSent, mailBody)
                : _graph.SendAsync(mailToBeSent, mailBody, blocksTenantId!);
        }
    }
}
