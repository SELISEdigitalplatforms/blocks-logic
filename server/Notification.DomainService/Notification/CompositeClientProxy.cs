using Microsoft.AspNetCore.SignalR;

namespace DomainService.Notification
{
    /// <summary>Sends one message to several client selections, e.g. stored connections plus groups.</summary>
    public sealed class CompositeClientProxy : IClientProxy
    {
        private readonly IClientProxy[] _proxies;

        public CompositeClientProxy(params IClientProxy[] proxies)
        {
            _proxies = proxies;
        }

        public Task SendCoreAsync(string method, object?[] args, CancellationToken cancellationToken = default) =>
            Task.WhenAll(_proxies.Select(proxy => proxy.SendCoreAsync(method, args, cancellationToken)));
    }
}
