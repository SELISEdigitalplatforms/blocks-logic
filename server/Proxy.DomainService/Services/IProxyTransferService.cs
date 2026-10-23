using Proxy.DomainService.Dtos;

namespace Proxy.DomainService.Services
{
    /// <summary>
    /// One proxy per file, identical across environments (2026-10-18): export, a preview that writes nothing, and
    /// apply. All scoped to the caller's tenant; ids never collide because every environment is its own database.
    /// </summary>
    public interface IProxyTransferService
    {
        /// <summary>The file for one proxy; 404 when the id is not in this tenant.</summary>
        Task<ProxyTransferResponse<ProxyExportFile>> ExportAsync(string tenantId, string itemId);

        /// <summary>What importing <paramref name="file"/> would do here. Writes nothing.</summary>
        Task<ProxyTransferResponse<ProxyImportResultDto>> PreviewAsync(string tenantId, ProxyExportFile? file);

        /// <summary>Runs every preview check again, then inserts, updates, or does nothing.</summary>
        Task<ProxyTransferResponse<ProxyImportResultDto>> ApplyAsync(string tenantId, ProxyImportApplyRequestDto request);
    }
}
