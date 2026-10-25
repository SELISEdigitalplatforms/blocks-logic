using Blocks.Genesis;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace DomainService.Notification
{
    /// <summary>How the send actions treat a call that has neither a valid sender secret nor the send permission.</summary>
    public enum NotifierSendAuthMode
    {
        /// <summary>No check.</summary>
        Off,
        /// <summary>The call goes through; it is logged.</summary>
        Report,
        /// <summary>The call is refused. The default.</summary>
        Enforce,
    }

    /// <summary>What the caller's bearer token says about the send permission.</summary>
    public enum NotifierCallerToken
    {
        /// <summary>No token was sent.</summary>
        None,
        /// <summary>A token was sent but it is not valid (bad signature, expired, other tenant ...).</summary>
        Invalid,
        /// <summary>A valid token without the send permission.</summary>
        NoPermission,
        /// <summary>A valid token with the send permission.</summary>
        Permitted,
    }

    /// <summary>Decides whether a call to a notifier send action may go through.</summary>
    public interface INotifierSendGuard
    {
        /// <summary>
        /// True when the call may go through: a valid <c>Secret</c> header OR a token with the send permission.
        /// <paramref name="secret"/> is the raw <c>Secret</c> header; <paramref name="checkToken"/> is only called
        /// when the secret is not valid, so a sender with the secret costs no permission lookup.
        /// </summary>
        Task<bool> IsAllowedAsync(string? secret, Func<Task<NotifierCallerToken>> checkToken, string action);
    }

    /// <summary>
    /// A call may go through when EITHER its <c>Secret</c> header is the hash the Blocks services already send (the
    /// request tenant id with that tenant's salt) OR its bearer token has the send permission. The mode comes from
    /// <c>Notifier:SendAuthMode</c> on every call, so it can be switched without a restart; missing or unknown means
    /// <see cref="NotifierSendAuthMode.Enforce"/>. Off and Report are explicit opt-outs.
    /// </summary>
    public sealed class NotifierSendGuard : INotifierSendGuard
    {
        public const string ModeConfigKey = "Notifier:SendAuthMode";

        private readonly IConfiguration _configuration;
        private readonly ITenants _tenants;
        private readonly ICryptoService _cryptoService;
        private readonly ILogger<NotifierSendGuard> _logger;

        public NotifierSendGuard(IConfiguration configuration, ITenants tenants, ICryptoService cryptoService,
            ILogger<NotifierSendGuard> logger)
        {
            _configuration = configuration;
            _tenants = tenants;
            _cryptoService = cryptoService;
            _logger = logger;
        }

        public NotifierSendAuthMode Mode =>
            Enum.TryParse<NotifierSendAuthMode>(_configuration[ModeConfigKey], ignoreCase: true, out var mode)
                && Enum.IsDefined(mode)
                ? mode
                : NotifierSendAuthMode.Enforce;

        public Task<bool> IsAllowedAsync(string? secret, Func<Task<NotifierCallerToken>> checkToken, string action)
        {
            ArgumentNullException.ThrowIfNull(checkToken);
            return IsAllowedCoreAsync(secret, checkToken, action);
        }

        private async Task<bool> IsAllowedCoreAsync(string? secret, Func<Task<NotifierCallerToken>> checkToken, string action)
        {
            var mode = Mode;
            if (mode == NotifierSendAuthMode.Off)
                return true;

            var tenantId = BlocksContext.GetContext()?.TenantId ?? string.Empty;
            var failure = FindFailure(secret, tenantId);
            if (failure is null)
                return true;

            var token = await checkToken();
            if (token == NotifierCallerToken.Permitted)
                return true;

            // Never the header value or the token: only what is needed to find the caller.
            _logger.LogWarning(
                "Notifier {Action} called without a valid sender secret ({Failure}) or send permission (token {Token}) for tenant {TenantId}; mode {Mode}",
                action, failure, token, tenantId, mode);
            return mode != NotifierSendAuthMode.Enforce;
        }

        private string? FindFailure(string? secret, string tenantId)
        {
            if (string.IsNullOrEmpty(secret))
                return "missing";
            if (string.IsNullOrWhiteSpace(tenantId))
                return "no tenant";

            var salt = _tenants.GetTenantByID(tenantId)?.TenantSalt;
            if (string.IsNullOrEmpty(salt))
                return "unknown tenant";

            return _cryptoService.ConstantTimeEquals(secret, _cryptoService.Hash(tenantId, salt)) ? null : "mismatch";
        }
    }
}
