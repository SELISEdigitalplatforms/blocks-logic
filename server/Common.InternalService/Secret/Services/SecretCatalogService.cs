using Blocks.Secrets;
using Microsoft.Extensions.Logging;

namespace Common.InternalService.Secret.Services
{
    /// <inheritdoc cref="ISecretCatalogService" />
    public sealed class SecretCatalogService : ISecretCatalogService
    {
        /// <summary>
        /// The Blocks Secrets types that count as <b>platform</b> secrets: the ones a backend module can
        /// actually resolve at execution time. An <c>api</c>-typed secret is reachable only through its own
        /// access list, so it is not a platform secret and never reaches a picker.
        /// </summary>
        /// <remarks>
        /// <c>SecretFilter.Type</c> takes a single value, so this narrowing is applied after the read rather
        /// than pushed into the filter. Keep this set as the single place the platform category is defined.
        /// </remarks>
        private static readonly HashSet<string> PlatformTypes =
            new(StringComparer.OrdinalIgnoreCase) { SecretTypes.Service, SecretTypes.Both };

        /// <summary>A picker shows the whole tenant list; keep it to one page well above any realistic count.</summary>
        private const int PageSize = 500;

        private readonly ISecretService _secrets;
        private readonly ILogger<SecretCatalogService> _logger;

        public SecretCatalogService(ISecretService secrets, ILogger<SecretCatalogService> logger)
        {
            _secrets = secrets;
            _logger = logger;
        }

        public async Task<SecretListResponse> GetAllAsync(
            GetSecretsRequest request, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(request);

            var tag = Trim(request.Tag);

            var filter = new SecretFilter
            {
                Search = Trim(request.Search),
                Tags = tag is null ? null : new[] { tag },
                PageSize = PageSize,
                PageNumber = 0,
            };

            SecretListResult result;
            try
            {
                result = await _secrets.FindAsync(filter, cancellationToken);
            }
            catch (SecretException ex)
            {
                // Vault unreachable / access problem: a picker renders this as a disabled control with an
                // "Unable to load" tooltip, so surface an empty list rather than a 500.
                _logger.LogWarning(ex, "Secret catalog: secret list read failed.");
                return new SecretListResponse();
            }

            var name = Trim(request.Name);

            var rows = (result.Data ?? Array.Empty<SecretResult>())
                .Where(s => PlatformTypes.Contains(s.Type ?? string.Empty))
                .Where(s => name is null || string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase))
                .Select(s => new SecretListItem
                {
                    Id = s.SecretId ?? string.Empty,
                    Name = s.Name ?? string.Empty,
                    Tags = s.Tags is null ? new List<string>() : s.Tags.ToList(),
                })
                .ToList();

            return new SecretListResponse { Data = rows, TotalCount = rows.Count };
        }

        private static string? Trim(string? value) =>
            string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
