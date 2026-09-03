using Mail.DomainService.Dtos;

namespace Mail.DomainService.Mails
{
    public interface ISendMailService
    {
        /// <summary>Sends the persisted mail. Returns false when the SMTP send did not succeed.</summary>
        Task<bool> ProcessSendMailAsync(SendEmailEvent sendEmailEvent);
    }
}
