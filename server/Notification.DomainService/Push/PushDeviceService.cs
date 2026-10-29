using Blocks.Genesis;

namespace DomainService.Notification.Push
{
    /// <summary>Body of <c>POST Notifier/devices</c>.</summary>
    public sealed class RegisterDeviceRequest
    {
        public string? Token { get; set; }

        /// <summary><c>android</c>, <c>ios</c> or <c>web</c>.</summary>
        public string? Platform { get; set; }

        /// <summary>Optional device model for the admin list (for example "Pixel 8"), at most 100 characters.</summary>
        public string? Name { get; set; }
    }

    /// <summary>The caller's own push devices.</summary>
    public interface IPushDeviceService
    {
        Task<BaseResponse> RegisterAsync(RegisterDeviceRequest request, CancellationToken cancellationToken);

        /// <summary>False when the caller has no device with this token (another user's token is never deleted).</summary>
        Task<bool> RemoveAsync(string token, CancellationToken cancellationToken);

        /// <summary>The caller's DB and user: the ROOT DB for an impersonated root user.</summary>
        (PushDb Db, string UserId) Caller();

        /// <summary>The context tenant's own DB (never the ROOT DB), for project-wide device reads.</summary>
        PushDb TenantDb();
    }

    /// <inheritdoc cref="IPushDeviceService"/>
    public sealed class PushDeviceService : IPushDeviceService
    {
        public const int MaxDevicesPerUser = 10;
        public const int MaxTokenLength = 4096;
        public const int MaxNameLength = 100;

        private readonly IPushDeviceStore _devices;
        private readonly TimeProvider _timeProvider;

        public PushDeviceService(IPushDeviceStore devices, TimeProvider timeProvider)
        {
            _devices = devices;
            _timeProvider = timeProvider;
        }

        public Task<BaseResponse> RegisterAsync(RegisterDeviceRequest request, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(request);
            var errors = Validate(request);
            if (errors.Count > 0)
                return Task.FromResult(new BaseResponse { IsSuccess = false, Errors = errors });

            var name = string.IsNullOrWhiteSpace(request.Name) ? null : request.Name.Trim();
            return RegisterCoreAsync(request.Token!.Trim(), PushPlatforms.Normalize(request.Platform)!, name, cancellationToken);
        }

        private async Task<BaseResponse> RegisterCoreAsync(string token, string platform, string? name, CancellationToken cancellationToken)
        {
            var (db, userId) = Caller();
            var now = _timeProvider.GetUtcNow().UtcDateTime;
            await _devices.UpsertAsync(db, new NotificationDevice
            {
                Id = NotificationDevice.IdOf(token),
                UserId = userId,
                Token = token,
                Platform = platform,
                Name = name,
                CreatedAt = now,
                LastSeenAt = now,
            }, cancellationToken);
            await _devices.TrimAsync(db, userId, MaxDevicesPerUser, cancellationToken);
            return new BaseResponse { IsSuccess = true };
        }

        public Task<bool> RemoveAsync(string token, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(token) || token.Length > MaxTokenLength)
                return Task.FromResult(false);

            var (db, userId) = Caller();
            return _devices.DeleteOwnAsync(db, userId, NotificationDevice.IdOf(token.Trim()), cancellationToken);
        }

        public (PushDb Db, string UserId) Caller()
        {
            var context = BlocksContext.GetContext();
            if (context is null || string.IsNullOrWhiteSpace(context.UserId) || string.IsNullOrWhiteSpace(context.TenantId))
                throw new InvalidOperationException("A signed-in user is required.");

            return (new PushDb(context.TenantId, context.Impersonated), context.UserId);
        }

        public PushDb TenantDb()
        {
            var context = BlocksContext.GetContext();
            if (context is null || string.IsNullOrWhiteSpace(context.TenantId))
                throw new InvalidOperationException("A tenant context is required.");

            return new PushDb(context.TenantId, Root: false);
        }

        private static Dictionary<string, string> Validate(RegisterDeviceRequest request)
        {
            var errors = new Dictionary<string, string>();
            if (string.IsNullOrWhiteSpace(request.Token))
                errors[nameof(request.Token)] = "Token is required.";
            else if (request.Token.Trim().Length > MaxTokenLength)
                errors[nameof(request.Token)] = $"Token is longer than {MaxTokenLength} characters.";

            if (PushPlatforms.Normalize(request.Platform) is null)
                errors[nameof(request.Platform)] = "Platform must be android, ios or web.";

            if (request.Name?.Trim().Length > MaxNameLength)
                errors[nameof(request.Name)] = $"Name is longer than {MaxNameLength} characters.";

            return errors;
        }
    }
}
