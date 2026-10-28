using Blocks.Genesis;
using Blocks.Secrets;
using Microsoft.Extensions.Logging;

namespace DomainService.Notification.Push
{
    /// <summary>Answer of <c>GET Notifier/push-settings</c>. Never the key.</summary>
    public sealed record PushSettingsView(string? ProjectId, bool Enabled, bool HasKey, DateTime? KeyUploadedAt, string? KeyUploadedBy);

    /// <summary>Body of <c>POST Notifier/push-settings</c>. <see cref="ServiceAccountJson"/> only when a new key is uploaded.</summary>
    public sealed class SavePushSettingsRequest
    {
        public string? ProjectId { get; set; }
        public bool Enabled { get; set; }
        public string? ServiceAccountJson { get; set; }
    }

    /// <summary>The project's mobile push (Firebase) settings.</summary>
    public interface IPushSettingsService
    {
        Task<PushSettingsView> GetAsync(CancellationToken cancellationToken);
        Task<BaseResponse> SaveAsync(SavePushSettingsRequest request, CancellationToken cancellationToken);

        /// <summary>Pushes a fixed text to the caller's own devices.</summary>
        Task<BaseResponse> SendTestAsync(CancellationToken cancellationToken);
    }

    /// <inheritdoc cref="IPushSettingsService"/>
    public sealed class PushSettingsService : IPushSettingsService
    {
        public const int MaxProjectIdLength = 100;
        internal const string TestTitle = "Test notification";
        internal const string TestBody = "Mobile push works for this project.";
        private const string KeyField = "serviceAccountJson";
        private const string ProjectField = "projectId";

        private readonly IPushSettingsStore _store;
        private readonly IPushCredentialStore _credentials;
        private readonly IPushDeviceService _devices;
        private readonly IPushTestSender _testSender;
        private readonly TimeProvider _timeProvider;
        private readonly ILogger<PushSettingsService> _logger;

        public PushSettingsService(IPushSettingsStore store, IPushCredentialStore credentials, IPushDeviceService devices,
            IPushTestSender testSender, TimeProvider timeProvider, ILogger<PushSettingsService> logger)
        {
            _store = store;
            _credentials = credentials;
            _devices = devices;
            _testSender = testSender;
            _timeProvider = timeProvider;
            _logger = logger;
        }

        public async Task<PushSettingsView> GetAsync(CancellationToken cancellationToken)
        {
            var record = await _store.GetAsync(cancellationToken);
            return record is null
                ? new PushSettingsView(null, false, false, null, null)
                : new PushSettingsView(record.ProjectId, record.Enabled, record.HasKey, record.KeyUploadedAt, record.KeyUploadedBy);
        }

        public Task<BaseResponse> SaveAsync(SavePushSettingsRequest request, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(request);
            return SaveCoreAsync(request, cancellationToken);
        }

        private async Task<BaseResponse> SaveCoreAsync(SavePushSettingsRequest request, CancellationToken cancellationToken)
        {
            var stored = await _store.GetAsync(cancellationToken) ?? new PushSettingsRecord();
            var projectId = request.ProjectId?.Trim() ?? string.Empty;
            var hasNewKey = !string.IsNullOrWhiteSpace(request.ServiceAccountJson);

            var errors = PushSettingsRules.Check(projectId, request.Enabled, request.ServiceAccountJson, stored);
            if (errors.Count > 0)
                return Failed(errors);

            var record = new PushSettingsRecord
            {
                ProjectId = projectId,
                Enabled = request.Enabled,
                SecretName = string.IsNullOrWhiteSpace(stored.SecretName) ? new PushSettingsRecord().SecretName : stored.SecretName,
                KeyUploadedAt = stored.KeyUploadedAt,
                KeyUploadedBy = stored.KeyUploadedBy,
            };

            if (hasNewKey)
            {
                if (!await TryStoreKeyAsync(record.SecretName, request.ServiceAccountJson!, cancellationToken))
                    return Failed(new Dictionary<string, string> { [KeyField] = "The key could not be stored. Try again." });

                record.KeyUploadedAt = _timeProvider.GetUtcNow().UtcDateTime;
                record.KeyUploadedBy = BlocksContext.GetContext()?.UserId;
            }

            await _store.SaveAsync(record, cancellationToken);
            return new BaseResponse { IsSuccess = true };
        }

        public async Task<BaseResponse> SendTestAsync(CancellationToken cancellationToken)
        {
            var settings = await _store.GetAsync(cancellationToken);
            if (settings is not { CanSend: true })
                return Failed(new Dictionary<string, string> { ["push"] = "Mobile push is off or not set up." });

            var (db, userId) = _devices.Caller();
            var sent = await _testSender.SendAsync(db, userId, settings, TestTitle, TestBody, cancellationToken);
            return sent switch
            {
                null => Failed(new Dictionary<string, string> { ["devices"] = "No device is registered for your user." }),
                0 => Failed(new Dictionary<string, string> { ["push"] = "The test push was not delivered." }),
                _ => new BaseResponse { IsSuccess = true },
            };
        }

        private async Task<bool> TryStoreKeyAsync(string secretName, string json, CancellationToken cancellationToken)
        {
            try
            {
                await _credentials.WriteAsync(secretName, json, cancellationToken);
                return true;
            }
            catch (SecretException ex)
            {
                // Type only: never the key, and the message can name the secret.
                _logger.LogWarning("Push settings: the key was not stored ({ErrorType})", ex.GetType().Name); // NOSONAR S6667 — type only
                return false;
            }
        }

        private static BaseResponse Failed(Dictionary<string, string> errors) => new() { IsSuccess = false, Errors = errors };

        /// <summary>Field rules of a save. Error texts never hold any part of the key.</summary>
        internal static class PushSettingsRules
        {
            public static Dictionary<string, string> Check(string projectId, bool enabled, string? serviceAccountJson, PushSettingsRecord stored)
            {
                var errors = new Dictionary<string, string>();
                if (ProjectIdError(projectId, enabled || !string.IsNullOrWhiteSpace(serviceAccountJson)) is { } projectError)
                    errors[ProjectField] = projectError;

                if (!string.IsNullOrWhiteSpace(serviceAccountJson))
                {
                    var key = ServiceAccountKey.TryParse(serviceAccountJson, out var keyError);
                    if (key is null)
                        errors[KeyField] = keyError!;
                    else if (!string.Equals(key.ProjectId, projectId, StringComparison.Ordinal))
                        errors[ProjectField] = "The key belongs to another Firebase project.";
                }
                else if (enabled && !stored.HasKey)
                {
                    errors[KeyField] = "Upload a service account key before turning push on.";
                }
                else if (stored.HasKey && !string.IsNullOrEmpty(projectId) && !string.Equals(stored.ProjectId, projectId, StringComparison.Ordinal))
                {
                    errors[KeyField] = "Upload the key of the new Firebase project.";
                }

                return errors;
            }

            /// <summary>Firebase project ids: lowercase letters, digits and dashes.</summary>
            private static string? ProjectIdError(string projectId, bool required)
            {
                if (projectId.Length == 0)
                    return required ? "Project id is required." : null;
                if (projectId.Length > MaxProjectIdLength || !projectId.All(c => c is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '-'))
                    return "Project id may hold only lowercase letters, digits and dashes.";
                return null;
            }
        }
    }
}
