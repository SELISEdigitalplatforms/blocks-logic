using Blocks.Genesis;
using FluentValidation;
using Mail.DomainService.Dtos;
using Mail.DomainService.Entities;
using Mail.DomainService.Services;
using Mail.DomainService.Utilities;
using Mail.DomainService.Shared.Enums;
using Microsoft.Extensions.Logging;
using Blocks.Genesis.Entities.Logic;
using EmailTemplate = Blocks.Genesis.Entities.Os.EmailTemplate;

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

            var isTestMail = request?.IsTestMail ?? false;
            var mailToBeSent = isTestMail && !string.IsNullOrWhiteSpace(request!.TemplateId)
                ? await MapTestTemplateAsync(request)
                : await MapAsync(request!, CallerOrganization(), false, isTestMail);
            return await ProcessMailSent(mailToBeSent);
        }

        public Task<BaseMutationResponse> ProcessMailAsync(SendMail request) => ProcessSendMailAsync(request, CallerOrganization());

        // Queue producers (IAM invites, password resets, workflows, functions, ...) can set any context, so a queued
        // mail never picks an organization's template: it always uses the Default organization's.
        public Task<BaseMutationResponse> ProcessQueuedMailAsync(SendMail request) => ProcessSendMailAsync(request, MailOrganization.Default);

        private async Task<BaseMutationResponse> ProcessSendMailAsync(SendMail request, string organizationId)
        {
            LogRequested("Send", request);

            if (!request.SendPhoneNumberAsEmail)
            {
                return await ProcessMailSent(await MapAsync(request, organizationId, onlyUser: true));
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

            return await ProcessMailSent(await MapAsync(request, organizationId, onlyUser: false));
        }

        /// <summary>
        /// The organization in the caller's token, or the Default organization when it has none. No request field
        /// sets the organization.
        /// </summary>
        private static string CallerOrganization() => MailOrganization.Normalize(BlocksContext.GetContext()?.OrganizationId);

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

        public async Task<MailToBeSent> MapAsync(BaseMailRequest request, string organizationId, bool onlyUser = true, bool isTestMail = false)
        {
            if (BlocksContext.GetContext() is null)
            {
                _logger.LogError(
                    "MAIL MAPPING: no BlocksContext on this thread, so the tenant is unknown and the template lookup cannot run. purpose={Purpose}",
                    request.Purpose);
            }

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

            var templateChoice = await _mailRepository.GetEmailTemplateForOrganizationAsync(request.Purpose, request.Language, organizationId);
            if (templateChoice.TiedTemplateIds.Count > 1)
            {
                // Nothing makes Name + Language unique within an organization; the pick is deterministic.
                _logger.LogWarning(
                    "MAIL MAPPING: {Count} templates share purpose={Purpose} language={Language} in organization {TemplateOrganizationId}; using the lowest ItemId {TemplateId}. templateIds={TemplateIds} organizationId={OrganizationId}",
                    templateChoice.TiedTemplateIds.Count, request.Purpose, request.Language,
                    MailOrganization.Normalize(templateChoice.Template!.OrganizationId), templateChoice.Template.ItemId,
                    string.Join(",", templateChoice.TiedTemplateIds), organizationId);
            }

            return await CompleteMappingAsync(request, toUsers, ccUsers, bccUsers, templateChoice.Template, organizationId,
                request.Purpose, request.Language, isTestMail);
        }

        /// <summary>
        /// A test mail of one exact template: its own organization chooses the configuration, and its Purpose and
        /// Language are used. An unknown id keeps the request's Purpose (or the id itself) so the validator returns
        /// the existing "does not exist" message.
        /// </summary>
        private async Task<MailToBeSent> MapTestTemplateAsync(SendMailToAny request)
        {
            var template = await _mailRepository.GetEmailTemplateByIdAsync(request.TemplateId!);
            var organizationId = MailOrganization.Normalize(template?.OrganizationId);
            var purpose = template?.Name ?? (string.IsNullOrWhiteSpace(request.Purpose) ? request.TemplateId! : request.Purpose);
            var language = template?.Language ?? request.Language;

            return await CompleteMappingAsync(request, request.To, request.Cc, request.Bcc, template, organizationId, purpose, language, isTestMail: true);
        }

        private async Task<MailToBeSent> CompleteMappingAsync(BaseMailRequest request, IEnumerable<string> to, IEnumerable<string> cc,
            IEnumerable<string> bcc, EmailTemplate? template, string organizationId, string purpose, string language, bool isTestMail)
        {
            var configuration = template is null
                ? MailServerConfigurationChoice.None
                : await _mailRepository.GetMailServerConfigurationForSendAsync(template.MailConfigurationId, organizationId);
            var templateOrganizationId = template is null ? null : MailOrganization.Normalize(template.OrganizationId);

            if (template is null)
            {
                _logger.LogError(
                    "MAIL MAPPING: no EmailTemplate for purpose={Purpose} language={Language} organizationId={OrganizationId} templateOrganizationId={TemplateOrganizationId} configurationSource={ConfigurationSource}. Check the EmailTemplates collection.",
                    purpose, language, organizationId, templateOrganizationId, configuration.SourceName);
            }
            else if (configuration.Configuration is null)
            {
                _logger.LogError(
                    "MAIL MAPPING: no MailServerConfiguration for purpose={Purpose} language={Language} organizationId={OrganizationId} templateOrganizationId={TemplateOrganizationId} configurationSource={ConfigurationSource}. Check the template's MailConfigurationId, the organization default and the project default in the MailServerConfiguration collection; inbound configurations are never used.",
                    purpose, language, organizationId, templateOrganizationId, configuration.SourceName);
            }
            else
            {
                _logger.LogInformation(
                    "MAIL MAPPING: purpose={Purpose} language={Language} organizationId={OrganizationId} templateId={TemplateId} templateOrganizationId={TemplateOrganizationId} configurationId={ConfigurationId} configurationSource={ConfigurationSource} correlationId={CorrelationId}",
                    purpose, language, organizationId, template.ItemId, templateOrganizationId, configuration.Configuration.ItemId,
                    configuration.SourceName, request.CorrelationId);
            }

            return new MailToBeSent
            {
                ItemId = Guid.NewGuid().ToString(),
                To = to,
                Cc = cc,
                Bcc = bcc,

                BodyDataContext = request.BodyDataContext,
                Name = purpose,
                Language = language,
                Attachments = request.Attachments ?? Enumerable.Empty<string>(),// new string[] { },
                ReplyTo = request.ReplyTo,
                SubjectDataContext = request.SubjectDataContext,
                EmailTemplate = template!,
                MailServerConfiguration = configuration.Configuration!,
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
