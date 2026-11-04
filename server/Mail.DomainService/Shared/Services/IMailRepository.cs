using Mail.DomainService.Entities;
using Mail.DomainService.Mails;
using Blocks.Genesis.Entities.Logic;
using EmailTemplate = Blocks.Genesis.Entities.Os.EmailTemplate;

namespace Mail.DomainService.Services
{
    public interface IMailRepository
    {
        Task<bool> FileExists(string fileId);
        Task<List<string>> GetEmailAdressOfUsers(IEnumerable<string> emails);

        /// <summary>
        /// The phone-as-email addresses ("&lt;phone&gt;@&lt;gateway&gt;") whose phone part is the phone number
        /// of a user of the project; any other address is left out.
        /// </summary>
        Task<List<string>> GetPhoneEmailAddressesOfUsers(IEnumerable<string> addresses);

        /// <summary>
        /// The email of the user with this id in this tenant's <c>Users</c>, or null when there is none.
        /// </summary>
        Task<string?> GetUserEmailAsync(string tenantId, string userId);
        Task<MailServerConfiguration> GetMailServerConfigurationByTenantId(string tenantId);
        Task<List<MailServerConfigurationSummary>> GetMailServerConfigurationSummariesAsync();

        /// <summary>
        /// One read: the template with this Name and Language in <paramref name="organizationId"/>, else in the Default
        /// organization (missing or empty OrganizationId included). Ties go to the lowest ItemId.
        /// </summary>
        Task<EmailTemplateChoice> GetEmailTemplateForOrganizationAsync(string? purpose, string? language, string organizationId);

        /// <summary>One read: the template with this ItemId, or null.</summary>
        Task<EmailTemplate?> GetEmailTemplateByIdAsync(string templateId);

        /// <summary>
        /// One read: the first outbound configuration of the template's own, the organization's default, then the
        /// project default. Inbound configurations are never chosen.
        /// </summary>
        Task<MailServerConfigurationChoice> GetMailServerConfigurationForSendAsync(string? templateConfigurationId, string organizationId);
        Task<bool> MailTemplateForPurposeExists(string purpose, string language);
        Task<bool> MailServerConfigurationExists(string purpose, string language);
        Task<(List<MailBoxEntity> Mails, long TotalCount)> GetMailBoxMails(GetMailBoxMails request); //deprecated
        Task<(List<MailBoxEntityResponse> Mails, long TotalCount)> GetMailBoxAggregatedMails(GetMailBoxMails request);
        Task<MailBoxEntity> GetMailBoxMail(string messageId, string tenantId);
    }
}
