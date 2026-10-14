using Mail.DomainService.Entities;

namespace Mail.DomainService.Mails
{
    public interface ISendMailService
    {
        /// <summary>Sends the mail passed in memory (nothing is stored). Returns false when the send did not succeed.</summary>
        Task<bool> ProcessSendMailAsync(MailToBeSent mailToBeSent);
    }
}
