using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using Blocks.Genesis;
using Blocks.Genesis.Notifications;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace DataAccess.InternalService.Notifications;

/// <inheritdoc cref="IBuiltInNotificationSender"/>
/// <remarks>
/// Publishes through genesis <see cref="INotificationPublisher"/>: it returns at once and never throws, and the tenant's
/// configuration decides who receives the notification and on which channels. The named users are the users the event
/// is about.
/// </remarks>
public sealed class BuiltInNotificationSender : IBuiltInNotificationSender
{
    internal const string ThrottleSecondsSetting = "BuiltInNotification:ThrottleSeconds";
    internal const int DefaultThrottleSeconds = 300;
    internal const int MaxThrottleKeys = 1000;
    internal const int MaxDescriptionLength = 200;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly ConcurrentDictionary<string, DateTimeOffset> _lastSent = new();
    private readonly IConfiguration _configuration;
    private readonly INotificationPublisher _publisher;
    private readonly ILogger<BuiltInNotificationSender> _logger;
    private readonly TimeProvider _time;

    public BuiltInNotificationSender(
        IConfiguration configuration,
        INotificationPublisher publisher,
        ILogger<BuiltInNotificationSender> logger)
        : this(configuration, publisher, logger, TimeProvider.System)
    {
    }

    internal BuiltInNotificationSender(
        IConfiguration configuration,
        INotificationPublisher publisher,
        ILogger<BuiltInNotificationSender> logger,
        TimeProvider time)
    {
        _configuration = configuration;
        _publisher = publisher;
        _logger = logger;
        _time = time;
    }

    public Task<bool> SendAsync(
        string tenantId,
        string configurationName,
        IReadOnlyCollection<string>? userIds,
        BuiltInNotificationPayload payload,
        string? throttleKey = null,
        CancellationToken cancellationToken = default)
    {
        var users = userIds?.Where(u => !string.IsNullOrWhiteSpace(u)).Distinct(StringComparer.Ordinal).ToList() ?? [];
        if (users.Count == 0)
        {
            _logger.LogDebug("Built-in notification {ConfigurationName} skipped (tenant {TenantId}): no recipient", configurationName, tenantId);
            return Task.FromResult(false);
        }

        return Task.FromResult(Publish(tenantId, configurationName, users, payload, throttleKey));
    }

    /// <summary>
    /// No named user: who receives it is only the configuration's audience (for example "Selected" with an admin
    /// permission). With "Affected users only" nobody does.
    /// </summary>
    public Task<bool> SendToTenantAdminsAsync(
        string tenantId,
        string configurationName,
        BuiltInNotificationPayload payload,
        string? throttleKey = null,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(Publish(tenantId, configurationName, [], payload, throttleKey));

    private bool Publish(string tenantId, string configurationName, List<string> userIds, BuiltInNotificationPayload payload, string? throttleKey)
    {
        if (string.IsNullOrWhiteSpace(tenantId))
        {
            _logger.LogDebug("Built-in notification {ConfigurationName} skipped: no tenant", configurationName);
            return false;
        }

        if (IsThrottled(tenantId, configurationName, throttleKey))
        {
            _logger.LogDebug("Built-in notification {ConfigurationName} skipped (tenant {TenantId}): repeated within the throttle window", configurationName, tenantId);
            return false;
        }

        _publisher.Publish(new NotificationRequest
        {
            Name = configurationName,
            TenantId = tenantId,
            NamedUserIds = userIds,
            OrganizationId = ContextOrganization(tenantId),
            Payload = BuildPayload(payload),
            ResponseKey = configurationName,
            ResponseValue = payload.IsSuccess?.ToString() ?? "Sent",
        });
        return true;
    }

    /// <summary>The current context's organization, only when it is of the same tenant.</summary>
    private static string? ContextOrganization(string tenantId)
    {
        var context = BlocksContext.GetContext();
        return context is not null && string.Equals(context.TenantId, tenantId, StringComparison.Ordinal)
               && !string.IsNullOrWhiteSpace(context.OrganizationId)
            ? context.OrganizationId
            : null;
    }

    private static string BuildPayload(BuiltInNotificationPayload payload)
    {
        var fields = new Dictionary<string, object?>
        {
            ["event"] = payload.Event,
            ["title"] = payload.Title,
            ["description"] = Truncate(payload.Description),
        };
        if (payload.IsSuccess is { } ok)
        {
            fields["isSuccess"] = ok;
        }

        foreach (var (key, value) in payload.Ids ?? new Dictionary<string, string?>())
        {
            if (!string.IsNullOrWhiteSpace(key) && !fields.ContainsKey(key))
            {
                fields[key] = value;
            }
        }

        fields["occurredAt"] = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);
        return JsonSerializer.Serialize(fields, JsonOptions);
    }

    private static string Truncate(string text) =>
        text.Length <= MaxDescriptionLength ? text : text[..MaxDescriptionLength];

    /// <summary>True when the same alert was sent inside the window. Records the send otherwise.</summary>
    private bool IsThrottled(string tenantId, string configurationName, string? throttleKey)
    {
        if (string.IsNullOrWhiteSpace(throttleKey))
        {
            return false;
        }

        var seconds = int.TryParse(_configuration[ThrottleSecondsSetting], NumberStyles.None, CultureInfo.InvariantCulture, out var s) && s >= 0
            ? s
            : DefaultThrottleSeconds;
        var window = TimeSpan.FromSeconds(seconds);
        var now = _time.GetUtcNow();
        var key = $"{tenantId}|{configurationName}|{throttleKey}";

        if (_lastSent.Count >= MaxThrottleKeys)
        {
            foreach (var old in _lastSent.Where(p => now - p.Value >= window).Select(p => p.Key).ToList())
            {
                _lastSent.TryRemove(old, out _);
            }

            if (_lastSent.Count >= MaxThrottleKeys)
            {
                foreach (var oldest in _lastSent.OrderBy(p => p.Value).Take(MaxThrottleKeys / 2).Select(p => p.Key).ToList())
                {
                    _lastSent.TryRemove(oldest, out _);
                }
            }
        }

        var throttled = false;
        _lastSent.AddOrUpdate(
            key,
            now,
            (_, last) =>
            {
                if (now - last < window)
                {
                    throttled = true;
                    return last;
                }

                return now;
            });
        return throttled;
    }
}
