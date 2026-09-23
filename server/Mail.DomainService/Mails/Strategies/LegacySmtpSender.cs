using Mail.DomainService.Entities;
using Mail.DomainService.Shared.Enums;

namespace Mail.DomainService.Mails.Strategies
{
    /// <summary>
    /// The pre-existing username/password providers, routed through the unchanged
    /// <see cref="SmtpClientProvider"/>.
    /// </summary>
    /// <remarks>
    /// Deliberately a thin pass-through. Amazon SES and Zoho keep their transport selection, their
    /// password authentication, their <c>EnableSSL</c> handling and their SES headers byte for
    /// byte; the registry is the only thing that changed for them. The <c>SmtpClient</c> branch
    /// stays inside here rather than being lifted into the registry, because it is a property of
    /// these two providers and not a general routing concept.
    /// </remarks>
    public abstract class LegacySmtpSender : IOutboundMailSender
    {
        private readonly SmtpClientProvider _smtpClientProvider;

        protected LegacySmtpSender(SmtpClientProvider smtpClientProvider)
        {
            _smtpClientProvider = smtpClientProvider;
        }

        public abstract MailServiceProvider Provider { get; }

        public Task<bool> SendAsync(MailToBeSent mailToBeSent, MailBody mailBody) =>
            _smtpClientProvider.GetSmtpClient(mailToBeSent).SendAsync(mailToBeSent, mailBody);
    }

    public sealed class AmazonSesMailSender : LegacySmtpSender
    {
        public AmazonSesMailSender(SmtpClientProvider smtpClientProvider) : base(smtpClientProvider)
        {
        }

        public override MailServiceProvider Provider => MailServiceProvider.AmazonSes;
    }

    public sealed class ZohoMailSender : LegacySmtpSender
    {
        public ZohoMailSender(SmtpClientProvider smtpClientProvider) : base(smtpClientProvider)
        {
        }

        public override MailServiceProvider Provider => MailServiceProvider.Zoho;
    }
}
