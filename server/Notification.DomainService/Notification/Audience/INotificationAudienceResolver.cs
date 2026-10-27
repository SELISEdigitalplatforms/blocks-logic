using Blocks.Genesis.Entities.Os;
using DomainService.Shared;

namespace DomainService.Notification
{
    /// <summary>The users a send goes to after the configuration audience and the sender's lists are applied.</summary>
    /// <param name="UserIds">Final receivers: audience users, narrowed by the sender's lists when it gave any.</param>
    /// <param name="AudienceCount">Active users in the configuration audience.</param>
    /// <param name="DroppedRequestedCount">Sender user ids left out because they are not in the audience.</param>
    public sealed record AudienceResolution(IReadOnlyList<string> UserIds, int AudienceCount, int DroppedRequestedCount);

    /// <summary>Turns a configuration audience plus the sender's receiver lists into the final user ids.</summary>
    public interface INotificationAudienceResolver
    {
        Task<AudienceResolution> ResolveAsync(NotificationAudience audience, NotifierPayload sender, CancellationToken cancellationToken = default);
    }
}
