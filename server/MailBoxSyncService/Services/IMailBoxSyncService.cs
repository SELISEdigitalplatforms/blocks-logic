using Mail.DomainService.Entities;
using MailBoxSyncService.Entities;

namespace MailBoxSyncService.Services
{
    public interface IMailBoxSyncService
    {
        Task SyncInboxAsync(MailServerConfiguration config, string tenantId);
        Task SyncOutgoingAsync(SesEventNotification? sesEvent, string tenantId);
    }
}
