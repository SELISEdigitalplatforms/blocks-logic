using Blocks.Genesis;

namespace Mail.DomainService.Mails
{
    public interface IMailService
    {
        Task<BaseMutationResponse> ProcessMailToAnyAsync(SendMailToAny request);
        Task<BaseMutationResponse> ProcessMailAsync(SendMail request);

        /// <summary>
        /// A <see cref="SendMail"/> consumed from the mail queue. It always uses the Default organization's
        /// templates, whatever organization the message's context carries.
        /// </summary>
        Task<BaseMutationResponse> ProcessQueuedMailAsync(SendMail request);
        Task<GetMailBoxMailsResponse> GetMailBoxMailsAsync(GetMailBoxMails request);
        Task<GetMailBoxMailResponse> GetMailBoxMailAsync(GetMailBoxMail request);
        Task<List<MailServerConfigurationSummary>> GetMailServerConfigurationSummariesAsync();
    }
}
