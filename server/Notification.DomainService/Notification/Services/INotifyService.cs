using Blocks.Genesis;
using DomainService.Shared;

namespace DomainService.Notification
{
    /// <summary>The HTTP <c>Notify</c> send of apps and services.</summary>
    public interface INotifyService
    {
        /// <summary>
        /// Validates the request as before, then hands it to the genesis notification publisher and returns at once.
        /// Success means "accepted": receivers, channels and delivery are decided later by the configuration.
        /// </summary>
        Task<BaseResponse> NotifyAsync(NotifyRequest notifyRequest);
    }
}
