using Blocks.Genesis;
using Mail.DomainService.Dtos;
using Mail.DomainService.Entities;
using Mail.DomainService.Services;
using Mail.DomainService.Utilities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Net;

namespace Mail.DomainService.Mails
{
    public class SendMailService : ISendMailService
    {
        private readonly ILogger<SendMailService> _logger;
        private readonly IMailRepository _mailRepository;
        private readonly SmtpClientProvider _smtpClientProvider;
        private readonly IMailAttachmentResolver _attachmentResolver;
        private readonly IMessageClient _messageClient;
        private readonly MailStatusEventOptions _statusEventOptions;

        public SendMailService(
            ILogger<SendMailService> logger,
            IMailRepository mailRepository,
            SmtpClientProvider smtpClientProvider,
            IMailAttachmentResolver attachmentResolver,
            IMessageClient messageClient,
            IOptions<MailStatusEventOptions> statusEventOptions
        )
        {
            _logger = logger;
            _mailRepository = mailRepository;
            _smtpClientProvider = smtpClientProvider;
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

            var smtpClient = _smtpClientProvider.GetSmtpClient(mailToBeSent);
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

            var success = await smtpClient.SendAsync(mailToBeSent, mailBody);
            LogOutcome(success, mailToBeSent, mailBody.Attachments.Count);

            await PublishStatusAsync(
                mailToBeSent.ItemId,
                mailToBeSent.CorrelationId,
                mailToBeSent,
                mailBody.Attachments.Count,
                success ? null : "The SMTP server did not accept the message.");

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
                        SentOnUtc = DateTime.UtcNow,
                        ProjectKey = BlocksContext.GetContext()?.TenantId
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

        private void LogOutcome(bool success, MailToBeSent mailToBeSent, int attachmentCount)
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
