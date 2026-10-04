using Blocks.Genesis;
using Mail.DomainService.Dtos;
using Mail.DomainService.Entities;
using Mail.DomainService.Mails.Strategies;
using Mail.DomainService.Services;
using Mail.DomainService.Utilities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Net;

namespace Mail.DomainService.Mails
{
    public class SendMailService : ISendMailService
    {
        /// <summary>
        /// The generic failure every provider reports. Unchanged and deliberately uninformative:
        /// it is a public contract that consumers already branch on, and the detail belongs in
        /// logs rather than in an event that crosses a service boundary.
        /// </summary>
        private const string FailedToAcceptError = "The SMTP server did not accept the message.";

        private readonly ILogger<SendMailService> _logger;
        private readonly IMailRepository _mailRepository;
        private readonly IOutboundMailSenderRegistry _senderRegistry;
        private readonly IMailAttachmentResolver _attachmentResolver;
        private readonly IMessageClient _messageClient;
        private readonly MailStatusEventOptions _statusEventOptions;

        public SendMailService(
            ILogger<SendMailService> logger,
            IMailRepository mailRepository,
            IOutboundMailSenderRegistry senderRegistry,
            IMailAttachmentResolver attachmentResolver,
            IMessageClient messageClient,
            IOptions<MailStatusEventOptions> statusEventOptions
        )
        {
            _logger = logger;
            _mailRepository = mailRepository;
            _senderRegistry = senderRegistry;
            _attachmentResolver = attachmentResolver;
            _messageClient = messageClient;
            _statusEventOptions = statusEventOptions.Value;
        }

        public async Task<bool> ProcessSendMailAsync(SendEmailEvent sendEmailEvent)
        {
            var mailToBeSent = await _mailRepository.GetMailToBeSent(sendEmailEvent.ItemId);

            // SendEmailConsumer sends by id without going through validation, and the read here can
            // also miss a just-written document on a secondary. Everything below dereferences the
            // entity, so bail out with a diagnosable message rather than a NullReferenceException.
            if (mailToBeSent is null)
            {
                _logger.LogError(
                    "MAIL FAILED (not found): no MailToBeSent document for itemId={ItemId}. It was never persisted, or the read hit a replica that has not caught up.",
                    sendEmailEvent.ItemId);
                await PublishStatusAsync(sendEmailEvent.ItemId, null, null, 0, "The mail record could not be found.");
                return false;
            }

            if (mailToBeSent.MailServerConfiguration is null || mailToBeSent.EmailTemplate is null)
            {
                _logger.LogError(
                    "MAIL FAILED (incomplete): itemId={ItemId} purpose={Purpose} language={Language} is missing its {Missing}. The purpose and language pair did not resolve when the mail was mapped.",
                    mailToBeSent.ItemId,
                    mailToBeSent.Name,
                    mailToBeSent.Language,
                    mailToBeSent.MailServerConfiguration is null ? nameof(mailToBeSent.MailServerConfiguration) : nameof(mailToBeSent.EmailTemplate));
                await PublishStatusAsync(mailToBeSent.ItemId, mailToBeSent.CorrelationId, mailToBeSent, 0, "The mail is missing its template or server configuration.");
                return false;
            }

            // Provider first, before any transport is resolved. An unregistered provider is a
            // controlled failure here rather than a null reference escaping this method: the
            // resolver used to hand back null and the send dereferenced it, which took down the
            // consumer instead of reporting the send.
            if (!_senderRegistry.TryResolve(mailToBeSent.MailServerConfiguration.Provider, out var sender))
            {
                _logger.LogError(
                    "MAIL FAILED (provider): itemId={ItemId} has provider {Provider}, which no outbound sender is registered for. No secret or network access was attempted.",
                    mailToBeSent.ItemId,
                    mailToBeSent.MailServerConfiguration.Provider);
                await PublishStatusAsync(mailToBeSent.ItemId, mailToBeSent.CorrelationId, mailToBeSent, 0, FailedToAcceptError);
                return false;
            }

            var mailBody = BuildMailBody(mailToBeSent);

            try
            {
                mailBody.Attachments = await _attachmentResolver.ResolveAsync(mailToBeSent.Attachments);
            }
            catch (MailAttachmentException ex)
            {
                // Sending the mail without its attachment is the failure this change exists to
                // remove, so an unresolvable attachment fails the whole send.
                _logger.LogError(
                    ex,
                    "MAIL FAILED (attachments): itemId={ItemId} correlationId={CorrelationId} purpose={Purpose} reason={Reason}",
                    mailToBeSent.ItemId, mailToBeSent.CorrelationId, mailToBeSent.Name, ex.Message);
                await PublishStatusAsync(mailToBeSent.ItemId, mailToBeSent.CorrelationId, mailToBeSent, 0, ex.Message);
                return false;
            }

            var success = await sender.SendAsync(mailToBeSent, mailBody);
            LogOutcome(success, mailToBeSent, mailBody.Attachments.Count, sender.EmitsOwnFailureDiagnostic);

            await PublishStatusAsync(
                mailToBeSent.ItemId,
                mailToBeSent.CorrelationId,
                mailToBeSent,
                mailBody.Attachments.Count,
                success ? null : FailedToAcceptError);

            return success;
        }

        /// <summary>
        /// Tells whoever asked for the mail how it went. Every entry point funnels through here,
        /// so the API, the driver and both queue consumers all report identically.
        /// </summary>
        private async Task PublishStatusAsync(string itemId, string? correlationId, MailToBeSent? mail, int attachmentCount, string? error)
        {
            if (!_statusEventOptions.Enabled)
            {
                _logger.LogDebug("MAIL STATUS EVENT skipped for itemId={ItemId}: publishing is disabled.", itemId);
                return;
            }

            try
            {
                await _messageClient.SendToConsumerAsync(new ConsumerMessage<MailSentEvent>
                {
                    ConsumerName = _statusEventOptions.QueueName,
                    Payload = new MailSentEvent
                    {
                        ItemId = itemId,
                        CorrelationId = correlationId,
                        IsSuccess = error is null,
                        Error = error,
                        Purpose = mail?.Name,
                        Language = mail?.Language,
                        To = mail?.To ?? [],
                        AttachmentCount = attachmentCount,
                        SentOnUtc = DateTime.UtcNow
                    }
                });

                _logger.LogInformation(
                    "MAIL STATUS EVENT published to {Queue}: itemId={ItemId} correlationId={CorrelationId} success={IsSuccess} error={Error}",
                    _statusEventOptions.QueueName, itemId, correlationId, error is null, error ?? MailLog.None);
            }
            catch (Exception ex)
            {
                // A mail that was already handed to the SMTP server must not be reported as failed
                // just because the status event could not be published. If this is firing for every
                // mail, the queue is almost certainly not declared in the host's MessageConfiguration.
                _logger.LogError(
                    ex,
                    "MAIL STATUS EVENT FAILED for itemId={ItemId} to queue {Queue}: {ExceptionType}: {Message}. The mail itself was unaffected. If this repeats for every mail, the queue is not declared in the host's MessageConfiguration.",
                    itemId,
                    _statusEventOptions.QueueName,
                    ex.GetType().Name,
                    ex.Message);
            }
        }

        /// <param name="senderAlreadyDiagnosed">
        /// Whether the sender has already emitted its own classified error for this failure. A
        /// provider-neutral flag rather than a check on the provider id, so this stays free of a
        /// per-provider branch: a sender that says nothing still gets the summary below, and one
        /// that classifies its own failures is not reported twice with two different stories.
        /// </param>
        private void LogOutcome(bool success, MailToBeSent mailToBeSent, int attachmentCount, bool senderAlreadyDiagnosed = false)
        {
            var logMessage = string.Format(
                "MAIL {0}:\nItemId: {1}\nCorrelationId: {2}\nTo: HIDDEN recipients ({3})\nSubject: {4}\nTime: {5}\nTemplate Name: {6}\nAttachments: {7}",
                success ? "SUCCESS" : "FAILED",
                mailToBeSent.ItemId,
                mailToBeSent.CorrelationId ?? MailLog.None,
                MailLog.Recipients(mailToBeSent.To),
                mailToBeSent.EmailTemplate.TemplateSubject,
                DateTime.Now,
                mailToBeSent.EmailTemplate.Name,
                attachmentCount);

            if (success)
            {
                _logger.LogInformation("{LogMessage}", logMessage);
            }
            else if (senderAlreadyDiagnosed)
            {
                // Downgraded, not suppressed: the summary still carries the subject, template and
                // attachment count that the sender's classified error does not, but it stops
                // competing with it as a second top-level failure for one send.
                _logger.LogInformation("{LogMessage}", logMessage);
            }
            else
            {
                // The SMTP client already logged the exception with the host and the type; this
                // line ties that to the mail id so the two can be found together.
                _logger.LogError("{LogMessage}", logMessage);
            }
        }

        public MailBody BuildMailBody(MailToBeSent mailToBeSent)
        {
            return new MailBody
            {
                Subject = BuildSubject(mailToBeSent.EmailTemplate.TemplateSubject, mailToBeSent.SubjectDataContext),
                Body = BuildBody(mailToBeSent.EmailTemplate.TemplateBody, mailToBeSent.BodyDataContext)
            };
        }

        public static string BuildBody(string templateBody, Dictionary<string, string> placeHolderValues)
        {
            var body = templateBody;

            foreach (var placeHolderValue in placeHolderValues ?? [])
            {
                body = body.Replace("{{" + placeHolderValue.Key + "}}", WebUtility.HtmlEncode(placeHolderValue.Value));
            }

            return body;
        }

        public static string BuildSubject(string templateSubject, Dictionary<string, string> placeHolderValues)
        {
            var body = templateSubject;

            foreach (var placeHolderValue in placeHolderValues ?? [])
            {
                body = body.Replace("{{" + placeHolderValue.Key + "}}", placeHolderValue.Value);
            }

            return body;
        }
    }
}
