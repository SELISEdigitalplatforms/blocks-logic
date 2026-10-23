using Blocks.Genesis;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace DomainService.Notification
{
    /// <summary>How the send actions treat a call without a valid sender secret.</summary>
    public enum NotifierSendAuthMode
    {
        /// <summary>No check.</summary>
        Off,
        /// <summary>The call goes through; it is logged.</summary>
        Report,
        /// <summary>The call is refused.</summary>
        Enforce,
    }

    /// <summary>Decides whether a call to a notifier send action may go through.</summary>
    public interface INotifierSendGuard
    {
        /// <summary>True when the call may go through. <paramref name="secret"/> is the raw <c>Secret</c> header.</summary>
        bool IsAllowed(string? secret, string action);
    }

    /// <summary>
    /// Checks the <c>Secret</c> header the Blocks services already send: the hash of the request tenant id
    /// with that tenant's salt. The mode comes from <c>Notifier:SendAuthMode</c> on every call, so it can be
    /// switched without a restart; missing or unknown means <see cref="NotifierSendAuthMode.Report"/>.
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
                : NotifierSendAuthMode.Report;

        public bool IsAllowed(string? secret, string action)
        {
            var mode = Mode;
            if (mode == NotifierSendAuthMode.Off)
                return true;

            var tenantId = BlocksContext.GetContext()?.TenantId ?? string.Empty;
            var failure = FindFailure(secret, tenantId);
            if (failure is null)
                return true;

            // Never the header value: only what is needed to find the caller.
            _logger.LogWarning(
                "Notifier {Action} called without a valid sender secret ({Failure}) for tenant {TenantId}; mode {Mode}",
                action, failure, tenantId, mode);
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
