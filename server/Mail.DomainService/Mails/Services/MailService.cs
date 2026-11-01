using Blocks.Genesis;
using FluentValidation;
using Mail.DomainService.Dtos;
using Mail.DomainService.Entities;
using Mail.DomainService.Services;
using Mail.DomainService.Utilities;
using Mail.DomainService.Shared.Enums;
using Microsoft.Extensions.Logging;
using Blocks.Genesis.Entities.Logic;

namespace Mail.DomainService.Mails
{
    public class MailService : IMailService
    {
        private readonly IValidator<MailToBeSent> _validator;
        private readonly IMessageClient _messageClient;
        private readonly IMailRepository _mailRepository;
        private readonly ISendMailService _sendMailService;
        private readonly ILogger<MailService> _logger;

        public MailService(
            IValidator<MailToBeSent> validator,
            IMessageClient messageClient,
            IMailRepository mailRepository,
            ISendMailService sendMailService,
            ILogger<MailService> logger
        )
        {
            _validator = validator;
            _messageClient = messageClient;
            _mailRepository = mailRepository;
            _sendMailService = sendMailService;
            _logger = logger;
        }

        public async Task<BaseMutationResponse> ProcessMailToAnyAsync(SendMailToAny request)
        {
            LogRequested("SendToAny", request);

            // The console's "send test mail" button: it may reach only the person pressing it.
            if (request?.IsTestMail == true && !await IsOnlyTheCallerAsync(request))
            {
                return new BaseMutationResponse
                {
                    IsSuccess = false,
                    Errors = new Dictionary<string, string> { { "To", "A test mail can be sent only to your own email address." } }
                };
            }

            var mailToBeSent = await MapAsync(request!, false, request?.IsTestMail ?? false);
            return await ProcessMailSent(mailToBeSent);
        }

        public async Task<BaseMutationResponse> ProcessMailAsync(SendMail request)
        {
            LogRequested("Send", request);

            if (!request.SendPhoneNumberAsEmail)
            {
                return await ProcessMailSent(await MapAsync(request, onlyUser: true));
            }

            // Phone-as-email still reaches users only: each address must carry a user's phone number.
            // Before, this flag skipped the user check, so Send could mail any address.
            var to = await _mailRepository.GetPhoneEmailAddressesOfUsers(request.To ?? []);
            var cc = await _mailRepository.GetPhoneEmailAddressesOfUsers(request.Cc ?? []);
            var bcc = await _mailRepository.GetPhoneEmailAddressesOfUsers(request.Bcc ?? []);
            WarnIfRecipientsDropped("To", request.To, to);
            WarnIfRecipientsDropped("Cc", request.Cc, cc);
            WarnIfRecipientsDropped("Bcc", request.Bcc, bcc);
            request.To = to;
            request.Cc = cc;
            request.Bcc = bcc;

            return await ProcessMailSent(await MapAsync(request, onlyUser: false));
        }

        private async Task<bool> IsOnlyTheCallerAsync(BaseMailRequest request)
        {
            if (request.To is null || !request.To.Any())
            {
                return false;
            }

            var own = (await CallerEmailAsync())?.Trim();
            if (string.IsNullOrEmpty(own))
            {
                return false;
            }

            return request.To.Concat(request.Cc ?? []).Concat(request.Bcc ?? [])
                .All(address => string.Equals(address?.Trim(), own, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// The caller's own address. IAM access tokens carry no email claim, so a Blocks user's address is
        /// read from their user record; only a third-party token brings a mapped email of its own.
        /// </summary>
        private async Task<string?> CallerEmailAsync()
        {
            var context = BlocksContext.GetContext();
            if (!string.IsNullOrWhiteSpace(context?.Email))
            {
                return context.Email;
            }

            if (string.IsNullOrWhiteSpace(context?.UserId))
            {
                return null;
            }

            // An impersonating console user is a user of the root tenant, not of the project it acts in:
            // the same tenant Genesis reads the caller's permissions from.
            var tenantId = context.Impersonated && !string.IsNullOrEmpty(context.OriginalTenantId)
                ? context.OriginalTenantId
                : context.TenantId;

            return string.IsNullOrWhiteSpace(tenantId)
                ? null
                : await _mailRepository.GetUserEmailAsync(tenantId, context.UserId);
        }

        private void LogRequested(string mode, BaseMailRequest? request)
        {
            _logger.LogInformation(
                "MAIL REQUESTED: mode={Mode} purpose={Purpose} language={Language} to={To} attachments={AttachmentCount} correlationId={CorrelationId}",
                mode,
                request?.Purpose,
                request?.Language,
                MailLog.Recipients(request?.To),
                MailLog.Count(request?.Attachments),
                request?.CorrelationId);
        }

        public async Task<BaseMutationResponse> ProcessMailSent(MailToBeSent mailToBeSent)
        {
            var validationResult = await _validator.ValidateAsync(mailToBeSent);
            if (!validationResult.IsValid)
            {
                // Validation errors used to be returned to the caller and nowhere else, so a report
                // of "the mail did not send" left nothing in the log to go on.
                _logger.LogError(
                    "MAIL REJECTED (validation): itemId={ItemId} purpose={Purpose} language={Language} correlationId={CorrelationId} reasons={Reasons}",
                    mailToBeSent.ItemId,
                    mailToBeSent.Name,
                    mailToBeSent.Language,
                    mailToBeSent.CorrelationId,
                    string.Join(" | ", validationResult.Errors.Select(e => $"{e.PropertyName}: {e.ErrorMessage}")));

                return new BaseMutationResponse
                {
                    IsSuccess = false,
                    Errors = validationResult.Errors.ToDictionary(x => x.PropertyName, x => x.ErrorMessage)
                };
            }

            var result = await SendMailToBeSentAsync(mailToBeSent);
            if (result)
            {
                return new BaseMutationResponse { IsSuccess = true };
            }

            // Previously this always reported success, so an SMTP failure reached the caller as
            // 200 { IsSuccess: true }. The ItemId is the key to the log line that says why.
            return new BaseMutationResponse
            {
                IsSuccess = false,
                Errors = new Dictionary<string, string>
                {
                    { "Send", $"The mail was not sent. Mail id: {mailToBeSent.ItemId}" }
                }
            };
        }

        public async Task<MailToBeSent> MapAsync(BaseMailRequest request, bool onlyUser = true, bool isTestMail = false)
        {
            var bc = BlocksContext.GetContext();

            if (bc is null)
            {
                _logger.LogError(
                    "MAIL MAPPING: no BlocksContext on this thread, so the tenant is unknown and the template lookup cannot run. purpose={Purpose}",
                    request.Purpose);
            }

            var organizationId = bc?.OrganizationId;
            var toUsers = request.To;
            var ccUsers = request.Cc;
            var bccUsers = request.Bcc;

            if (onlyUser)
            {
                toUsers = await _mailRepository.GetEmailAdressOfUsers(request.To);
                ccUsers = await _mailRepository.GetEmailAdressOfUsers(request.Cc);
                bccUsers = await _mailRepository.GetEmailAdressOfUsers(request.Bcc);

                // This lookup drops anything that is not a registered user and reports nothing, so
                // a mail can reach fewer people than asked for and look entirely successful.
                WarnIfRecipientsDropped("To", request.To, toUsers);
                WarnIfRecipientsDropped("Cc", request.Cc, ccUsers);
                WarnIfRecipientsDropped("Bcc", request.Bcc, bccUsers);
            }

            var emailTemplate = await _mailRepository.GetEmailTemplateByPurpose(request.Purpose, request.Language, organizationId);
            var serverConfiguration = await _mailRepository.GetMailServerConfigurationByPurpose(request.Purpose, request.Language, organizationId);

            if (emailTemplate is null)
            {
                _logger.LogError(
                    "MAIL MAPPING: no EmailTemplate for purpose={Purpose} language={Language} organizationId={OrganizationId}. Check the EmailTemplates collection.",
                    request.Purpose, request.Language, organizationId);
            }

            if (serverConfiguration is null)
            {
                _logger.LogError(
                    "MAIL MAPPING: no MailServerConfiguration for purpose={Purpose} language={Language} organizationId={OrganizationId}. Check the template's MailConfigurationId and the MailServerConfiguration collection.",
                    request.Purpose, request.Language, organizationId);
            }

            return new MailToBeSent
            {
                ItemId = Guid.NewGuid().ToString(),
                To = toUsers,
                Cc = ccUsers,
                Bcc = bccUsers,

                BodyDataContext = request.BodyDataContext,
                Name = request.Purpose,
                Language = request.Language,
                Attachments = request.Attachments ?? Enumerable.Empty<string>(),// new string[] { },
                ReplyTo = request.ReplyTo,
                SubjectDataContext = request.SubjectDataContext,
                EmailTemplate = emailTemplate,
                MailServerConfiguration = serverConfiguration,
                IsTestMail = isTestMail,
                CorrelationId = request.CorrelationId
            };
        }

        private void WarnIfRecipientsDropped(string field, IEnumerable<string>? requested, IEnumerable<string>? resolved)
        {
            var asked = MailLog.Count(requested);
            var found = MailLog.Count(resolved);

            if (asked == found)
            {
                return;
            }

            _logger.LogWarning(
                "MAIL MAPPING: {Dropped} of {Asked} {Field} recipient(s) are not registered users and were dropped. requested={Requested}",
                asked - found, asked, field, MailLog.Recipients(requested));
        }

        private async Task<bool> SendMailToBeSentAsync(MailToBeSent mailToBeSent)
        {
            // "MAIL PERSISTED" now means "accepted for sending"; the mail is no longer stored.
            _logger.LogInformation(
                "MAIL PERSISTED: itemId={ItemId} purpose={Purpose} to={To} attachments={AttachmentCount} correlationId={CorrelationId}",
                mailToBeSent.ItemId,
                mailToBeSent.Name,
                MailLog.Recipients(mailToBeSent.To),
                MailLog.Count(mailToBeSent.Attachments),
                mailToBeSent.CorrelationId);

            return await _sendMailService.ProcessSendMailAsync(mailToBeSent);
        }

        public async Task SendToQueueAsync<T>(string queue, T payload) where T : class
        {
            await _messageClient.SendToConsumerAsync(new ConsumerMessage<T>
            {
                ConsumerName = queue,
                Payload = payload
            });
        }

        public async Task<GetMailBoxMailsResponse> GetMailBoxMailsAsync(GetMailBoxMails request)
        {

            if (!string.IsNullOrEmpty(request.Status) &&
                (!Enum.TryParse<MailStatus>(request.Status, true, out var status) ||
                 !CommunicationConstants.AllowedFilterStatuses.Contains(status)))
            {
                var allowed = string.Join(", ", CommunicationConstants.AllowedFilterStatuses);
                return new GetMailBoxMailsResponse
                {
                    IsSuccess = false,
                    Errors = new Dictionary<string, string>
                     {
                         { "Status", $"Invalid status: {request.Status}. Allowed values are: {allowed}" }
                     }
                };
            }

            var (mails, count) = await _mailRepository.GetMailBoxAggregatedMails(request);
            return new GetMailBoxMailsResponse
            {
                IsSuccess = true,
                Mails = mails,
                TotalCount = count
            };
        }

        public async Task<GetMailBoxMailResponse> GetMailBoxMailAsync(GetMailBoxMail request)
        {
            var mail = await _mailRepository.GetMailBoxMail(request.MessageId, BlocksContext.GetContext()?.TenantId);
            if (mail == null)
            {
                return new GetMailBoxMailResponse
                {
                    IsSuccess = false,
                    Errors = new Dictionary<string, string> { { "MessageId", "Mail not found" } }
                };
            }
            return new GetMailBoxMailResponse
            {
                IsSuccess = true,
                Mail = mail
            };
        }

        public Task<List<MailServerConfigurationSummary>> GetMailServerConfigurationSummariesAsync()
        {
            return _mailRepository.GetMailServerConfigurationSummariesAsync();
        }
    }
}
