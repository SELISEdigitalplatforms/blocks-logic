namespace DomainService.Notification
{
    public interface IWebPushVapidKeyService
    {
        Task<string> GetOrCreatePublicKeyAsync(CancellationToken cancellationToken = default);
        Task<(string PublicKey, string PrivateKey, string Subject)> GetVapidDetailsAsync(CancellationToken cancellationToken = default);
        Task RotateAsync(CancellationToken cancellationToken = default);
    }
}
