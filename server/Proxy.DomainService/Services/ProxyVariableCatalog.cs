using Blocks.Secrets;
using Microsoft.Extensions.Logging;
using Proxy.DomainService.Dtos;

namespace Proxy.DomainService.Services
{
    /// <summary>
    /// Read-only list of the caller-tenant's Blocks Secrets for the console's <c>{{$VAR.name}}</c> picker
    /// (<c>GET /api/Proxy/Variables</c>). Returns names / ids / type / tags only &mdash; never a value; the
    /// value is resolved on the forward / Test path by <see cref="IProxyVariableResolver"/> and never reaches
    /// the console.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="SeliseBlocks.Secrets.OS"/> ships <b>no HTTP surface</b> &mdash; it is a purely in-process
    /// library (<see cref="ISecretService"/>, supplied by <c>AddBlocksSecrets()</c>). This service is the
    /// thin control-plane endpoint that exposes the subset the picker needs.
    /// </para>
    /// <para>
    /// Unlike <see cref="ProxyVariableResolver"/> (which runs on the <c>X-Blocks-Key</c> data-plane and has
    /// to enter a tenant context by hand), this seam is only ever called from an <c>[Authorize]</c> action,
    /// so the ambient <c>BlocksContext</c> is already the caller's tenant and identity &mdash; no swap.
    /// </para>
    /// </remarks>
    public interface IProxyVariableCatalog
    {
        Task<ProxyVariableListResponseDto> ListAsync(string? search, CancellationToken ct = default);

        /// <summary>The tenant's secret-tag catalog (<c>key</c> / <c>label</c>), for a future picker filter.</summary>
        Task<IReadOnlyList<ProxyVariableTagDto>> ListTagsAsync(CancellationToken ct = default);
    }

    /// <inheritdoc cref="IProxyVariableCatalog" />
    public sealed class ProxyVariableCatalog : IProxyVariableCatalog
    {
        /// <summary>The picker shows the whole tenant list; keep it to one page well above any realistic count.</summary>
        private const int PageSize = 500;

        private readonly ISecretService _secrets;
        private readonly ILogger<ProxyVariableCatalog> _logger;

        public ProxyVariableCatalog(ISecretService secrets, ILogger<ProxyVariableCatalog> logger)
        {
            _secrets = secrets;
            _logger = logger;
        }

        public async Task<ProxyVariableListResponseDto> ListAsync(string? search, CancellationToken ct = default)
        {
            var filter = new SecretFilter
            {
                Search = string.IsNullOrWhiteSpace(search) ? null : search.Trim(),
                PageSize = PageSize,
                PageNumber = 0,
            };

            SecretListResult result;
            try
            {
                result = await _secrets.FindAsync(filter, ct);
            }
            catch (SecretException ex)
            {
                // Vault unreachable / access problem: the console renders this as a disabled picker with an
                // "Unable to load" tooltip, so surface an empty list rather than a 500.
                _logger.LogWarning(ex, "Proxy variable catalog: secret list read failed.");
                return new ProxyVariableListResponseDto();
            }

            var rows = (result.Data ?? Array.Empty<SecretResult>())
                .Select(s => new ProxyVariableDto
                {
                    SecretId = s.SecretId ?? string.Empty,
                    Name = s.Name ?? string.Empty,
                    Type = (s.Type ?? string.Empty).ToLowerInvariant(),
                    Tags = s.Tags is null ? new List<string>() : s.Tags.ToList(),
                })
                .ToList();

            return new ProxyVariableListResponseDto { Data = rows, TotalCount = result.TotalCount };
        }

        public async Task<IReadOnlyList<ProxyVariableTagDto>> ListTagsAsync(CancellationToken ct = default)
        {
            try
            {
                var tags = await _secrets.GetTagsAsync(ct);
                return tags
                    .Select(t => new ProxyVariableTagDto { Key = t.Key ?? string.Empty, Label = t.Label ?? string.Empty })
                    .ToList();
            }
            catch (SecretException ex)
            {
                _logger.LogWarning(ex, "Proxy variable catalog: tag catalog read failed.");
                return Array.Empty<ProxyVariableTagDto>();
            }
        }
    }
}
