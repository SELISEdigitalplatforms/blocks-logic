namespace MailBoxSyncService.Services
{
    public interface ISnsEventProcessor
    {
        Task ProcessAsync(HttpRequest request);
    }
}
