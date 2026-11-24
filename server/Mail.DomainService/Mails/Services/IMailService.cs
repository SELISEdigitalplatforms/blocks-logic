using Blocks.Genesis;

namespace Mail.DomainService.Mails
{
    public interface IMailService
    {
        Task<BaseMutationResponse> ProcessMailToAnyAsync(SendMailToAny request);
        Task<BaseMutationResponse> ProcessMailAsync(SendMail request);

        /// <summary>
        /// A <see cref="SendMail"/> consumed from the mail queue. Without <see cref="BaseMailRequest.OrganizationId"/>
        /// it uses the Default organization's templates, whatever organization the message's context carries. With it,
        /// a message without a delegation grant (a service send) uses that organization; one with a grant uses it only
        /// when the grant's organization may name it, and the Default organization otherwise.
        /// </summary>
        Task<BaseMutationResponse> ProcessQueuedMailAsync(SendMail request);
        Task<GetMailBoxMailsResponse> GetMailBoxMailsAsync(GetMailBoxMails request);
        Task<GetMailBoxMailResponse> GetMailBoxMailAsync(GetMailBoxMail request);
        Task<List<MailServerConfigurationSummary>> GetMailServerConfigurationSummariesAsync();
    }
}
