using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Blocks.Genesis;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace DataAccess.InternalService.Notifications;

/// <inheritdoc cref="IBuiltInNotificationSender"/>
public sealed class BuiltInNotificationSender : IBuiltInNotificationSender
{
    /// <summary>Hard limit for one send.</summary>
    internal static readonly TimeSpan SendTimeout = TimeSpan.FromSeconds(5);

    internal const string UrlSetting = "NotificationServiceUrl";
    internal const string AdminRolesSetting = "BuiltInNotification:AdminRoles";
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
    private readonly ITenants _tenants;
    private readonly ICryptoService _crypto;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<BuiltInNotificationSender> _logger;
    private readonly TimeProvider _time;

    public BuiltInNotificationSender(
        IConfiguration configuration,
        ITenants tenants,
        ICryptoService crypto,
        IHttpClientFactory httpClientFactory,
        ILogger<BuiltInNotificationSender> logger)
        : this(configuration, tenants, crypto, httpClientFactory, logger, TimeProvider.System)
    {
    }

    internal BuiltInNotificationSender(
        IConfiguration configuration,
        ITenants tenants,
        ICryptoService crypto,
        IHttpClientFactory httpClientFactory,
        ILogger<BuiltInNotificationSender> logger,
        TimeProvider time)
    {
        _configuration = configuration;
        _tenants = tenants;
        _crypto = crypto;
        _httpClientFactory = httpClientFactory;
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

        return SendCoreAsync(tenantId, configurationName, users, [], payload, throttleKey, cancellationToken);
    }

    public Task<bool> SendToTenantAdminsAsync(
        string tenantId,
        string configurationName,
        BuiltInNotificationPayload payload,
        string? throttleKey = null,
        CancellationToken cancellationToken = default)
    {
        var roles = AdminRoles();
        if (roles.Count == 0)
        {
            _logger.LogDebug("Built-in notification {ConfigurationName} skipped (tenant {TenantId}): no admin role configured", configurationName, tenantId);
            return Task.FromResult(false);
        }

        return SendCoreAsync(tenantId, configurationName, [], roles, payload, throttleKey, cancellationToken);
    }

    private async Task<bool> SendCoreAsync(
        string tenantId,
        string configurationName,
        List<string> userIds,
        List<string> roles,
        BuiltInNotificationPayload payload,
        string? throttleKey,
        CancellationToken cancellationToken)
    {
        string status;
        try
        {
            if (string.IsNullOrWhiteSpace(tenantId))
            {
                _logger.LogDebug("Built-in notification {ConfigurationName} skipped: no tenant", configurationName);
                return false;
            }

            var url = _configuration[UrlSetting];
            if (string.IsNullOrWhiteSpace(url))
            {
                _logger.LogWarning("Built-in notification {ConfigurationName} not sent (tenant {TenantId}): {Setting} is not set", configurationName, tenantId, UrlSetting);
                return false;
            }

            var salt = _tenants.GetTenantByID(tenantId)?.TenantSalt;
            if (string.IsNullOrWhiteSpace(salt))
            {
                _logger.LogWarning("Built-in notification {ConfigurationName} not sent (tenant {TenantId}): tenant salt not available", configurationName, tenantId);
                return false;
            }

            if (IsThrottled(tenantId, configurationName, throttleKey))
            {
                _logger.LogDebug("Built-in notification {ConfigurationName} skipped (tenant {TenantId}): repeated within the throttle window", configurationName, tenantId);
                return false;
            }

            var body = new
            {
                ConnectionId = string.Empty,
                Roles = roles,
                UserIds = userIds,
                DenormalizedPayload = BuildPayload(payload),
                SaveDenormalizedPayloadAsAnObject = false,
                ConfigurationName = configurationName,
                ContentAvailable = true,
                ResponseKey = configurationName,
                ResponseValue = payload.IsSuccess?.ToString() ?? "Sent",
            };

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(SendTimeout);

            var client = _httpClientFactory.CreateClient();
            using var request = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(JsonSerializer.Serialize(body, JsonOptions), Encoding.UTF8, "application/json"),
            };
            request.Headers.TryAddWithoutValidation("x-blocks-key", tenantId);
            request.Headers.TryAddWithoutValidation("Secret", _crypto.Hash(tenantId, salt));

            using var response = await client.SendAsync(request, timeout.Token);
            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("Built-in notification {ConfigurationName} sent (tenant {TenantId})", configurationName, tenantId);
                return true;
            }

            status = $"HTTP {(int)response.StatusCode}";
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            status = "timeout";
        }
        catch (Exception ex)
        {
            // Type only: an exception message can carry the URL or a header.
            status = ex.GetType().Name;
        }

        _logger.LogWarning("Built-in notification {ConfigurationName} not sent (tenant {TenantId}): {Status}", configurationName, tenantId, status);
        return false;
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

    private List<string> AdminRoles() =>
        (_configuration[AdminRolesSetting] ?? "admin")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.Ordinal)
            .ToList();

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
