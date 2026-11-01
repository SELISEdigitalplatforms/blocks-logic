namespace DataAccess.InternalService.Notifications;

/// <summary>What a built-in notification says. Ids and short fixed text only.</summary>
/// <param name="Event">Short event name, for example <c>WorkflowRunFailed</c>.</param>
/// <param name="Title">One short fixed sentence.</param>
/// <param name="Description">One short fixed sentence; cut at 200 characters.</param>
/// <param name="IsSuccess">Set when the event has a good and a bad outcome.</param>
/// <param name="Ids">Entity ids and short labels only, written as top-level camelCase fields.</param>
public sealed record BuiltInNotificationPayload(
    string Event,
    string Title,
    string Description,
    bool? IsSuccess = null,
    IReadOnlyDictionary<string, string?>? Ids = null);

/// <summary>
/// Sends one built-in notification (a configuration seeded in every tenant) through the genesis notification
/// publisher. Best effort: it never throws and never waits for I/O, so an action can call it after its own write
/// without risk.
/// </summary>
public interface IBuiltInNotificationSender
{
    /// <summary>Sends to named users. <paramref name="throttleKey"/> limits repeats of the same alert.</summary>
    /// <returns>True when the notification was queued (not skipped or throttled).</returns>
    Task<bool> SendAsync(
        string tenantId,
        string configurationName,
        IReadOnlyCollection<string>? userIds,
        BuiltInNotificationPayload payload,
        string? throttleKey = null,
        CancellationToken cancellationToken = default);

    /// <summary>Sends without named users: the configuration's audience decides who receives it (for example the
    /// users with an admin permission).</summary>
    Task<bool> SendToTenantAdminsAsync(
        string tenantId,
        string configurationName,
        BuiltInNotificationPayload payload,
        string? throttleKey = null,
        CancellationToken cancellationToken = default);
}
