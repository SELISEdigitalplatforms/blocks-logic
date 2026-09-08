using Blocks.Genesis;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Proxy.DomainService.Dtos;
using Proxy.DomainService.Services;

namespace Utilities.Api.Controllers
{
    /// <summary>
    /// Management API for named reverse proxies (control plane, Phase 1). Routed as <c>/api/Proxy/{action}</c>
    /// via the global <c>api</c> route prefix. Every action requires a bearer token; the tenant is taken from
    /// <see cref="BlocksContext"/>. Mutation results carry their own HTTP status (201 on create, 200 otherwise,
    /// 4xx on failure) so the service stays the single source of truth for the SPEC &sect;3.4 error contract.
    /// </summary>
    [ApiController]
    [Route("[controller]/[action]")]
    public sealed class ProxyController : ControllerBase
    {
        private readonly IProxyService _proxyService;
        private readonly IProxyVersionService _proxyVersionService;
        private readonly IProxyTestService _proxyTestService;
        private readonly IProxyExecutionService _proxyExecutionService;

        public ProxyController(
            IProxyService proxyService,
            IProxyVersionService proxyVersionService,
            IProxyTestService proxyTestService,
            IProxyExecutionService proxyExecutionService)
        {
            _proxyService = proxyService;
            _proxyVersionService = proxyVersionService;
            _proxyTestService = proxyTestService;
            _proxyExecutionService = proxyExecutionService;
        }

        [Authorize]
        [HttpPost]
        public async Task<IActionResult> GetAll([FromBody] ProxyGetAllRequestDto dto)
        {
            var result = await _proxyService.GetAllAsync(GetTenantId(), dto);
            return Ok(result);
        }

        [Authorize]
        [HttpGet]
        public async Task<IActionResult> Get([FromQuery] ProxyGetRequestDto dto)
        {
            var result = await _proxyService.GetAsync(GetTenantId(), dto);
            return Ok(result);
        }

        [Authorize]
        [HttpPost]
        public async Task<IActionResult> Create([FromBody] ProxyCreateRequestDto dto)
        {
            var result = await _proxyService.CreateAsync(GetTenantId(), dto);
            return StatusCode(result.HttpStatus, result);
        }

        [Authorize]
        [HttpPut]
        public async Task<IActionResult> Update([FromBody] ProxyUpdateRequestDto dto)
        {
            var result = await _proxyService.UpdateAsync(GetTenantId(), dto);
            return StatusCode(result.HttpStatus, result);
        }

        [Authorize]
        [HttpPost]
        public async Task<IActionResult> UpdateState([FromBody] ProxyToggleRequestDto dto)
        {
            var result = await _proxyService.ToggleAsync(GetTenantId(), dto);
            return StatusCode(result.HttpStatus, result);
        }

        [Authorize]
        [HttpDelete]
        public async Task<IActionResult> Delete([FromQuery] ProxyDeleteRequestDto dto)
        {
            var result = await _proxyService.DeleteAsync(GetTenantId(), dto);
            return StatusCode(result.HttpStatus, result);
        }

        [Authorize]
        [HttpPost]
        public async Task<IActionResult> GetVersions([FromBody] ProxyGetVersionsRequestDto dto)
        {
            var result = await _proxyVersionService.GetVersionsAsync(GetTenantId(), dto);
            return StatusCode(result.HttpStatus, result);
        }

        [Authorize]
        [HttpPost]
        public async Task<IActionResult> Revert([FromBody] ProxyRevertRequestDto dto)
        {
            var result = await _proxyVersionService.RevertAsync(GetTenantId(), dto);
            return StatusCode(result.HttpStatus, result);
        }

        /// <summary>
        /// Console-only: run the data-plane forward pipeline against a saved proxy or an unsaved draft and
        /// return the result WITHOUT recording a <c>ProxyExecutions</c> row and WITHOUT creating or mutating
        /// any <c>Proxies</c> / <c>ProxyVersions</c> data (SPEC &sect;3.3). The tenant is taken from
        /// <see cref="BlocksContext"/>; no <c>X-Blocks-Key</c> is required here.
        /// </summary>
        [Authorize]
        [HttpPost]
        public async Task<IActionResult> Test([FromBody] ProxyTestRequestDto dto)
        {
            var context = BlocksContext.GetContext();
            var outcome = await _proxyTestService.TestAsync(
                context?.TenantId ?? string.Empty, context?.UserId, dto);

            return outcome.IsSuccess
                ? Ok(outcome.Result)
                : StatusCode(outcome.HttpStatus, new { code = outcome.Code, errors = outcome.Errors });
        }

        /// <summary>
        /// SPEC3 &sect;3.1 &mdash; the <em>Request logs</em> list for one proxy: last 24h, filtered by
        /// <c>statusClass</c>, newest-first, paged, or a Live tail when <c>afterId</c> is supplied. Returns
        /// 400 <c>PROXY_VALIDATION</c> on a bad field and 404 <c>PROXY_NOT_FOUND</c> for a genuinely unknown
        /// proxy id (a deleted proxy whose rows remain still works).
        /// </summary>
        [Authorize]
        [HttpPost]
        public async Task<IActionResult> GetExecutions([FromBody] ProxyGetExecutionsRequestDto dto)
        {
            var result = await _proxyExecutionService.GetExecutionsAsync(GetTenantId(), dto);
            return StatusCode(result.HttpStatus, result);
        }

        /// <summary>
        /// SPEC3 &sect;3.2 &mdash; one expanded request-log row including the full stored response body,
        /// clipped to 64 KB for transport (the stored row is untouched). An unknown id, an id whose
        /// <c>proxyId</c> differs, or another tenant's row all return 200 with <c>data: null</c>.
        /// </summary>
        [Authorize]
        [HttpGet]
        public async Task<IActionResult> GetExecution([FromQuery] ProxyGetExecutionRequestDto dto)
        {
            var result = await _proxyExecutionService.GetExecutionAsync(GetTenantId(), dto);
            return StatusCode(result.HttpStatus, result);
        }

        /// <summary>
        /// SPEC3 &sect;3.3 &mdash; the Overview tiles for one proxy (calls / avg latency / error rate over the
        /// rolling 24h window) plus the Configuration-panel convenience fields. 404 <c>PROXY_NOT_FOUND</c>
        /// only when the id neither exists for the tenant nor has any execution rows.
        /// </summary>
        [Authorize]
        [HttpPost]
        public async Task<IActionResult> GetOverview([FromBody] ProxyGetOverviewRequestDto dto)
        {
            var result = await _proxyExecutionService.GetOverviewAsync(GetTenantId(), dto);
            return StatusCode(result.HttpStatus, result);
        }

        /// <summary>
        /// SPEC3 &sect;3.4 &mdash; the filtered last-24h log as a UTF-8 (BOM) RFC 4180 CSV attachment, newest
        /// first, capped at 50,000 rows (<c>X-Proxy-Export-Truncated: true</c> when more matched). Response
        /// bodies are never included.
        /// </summary>
        [Authorize]
        [HttpGet]
        public async Task<IActionResult> ExportExecutionsCsv([FromQuery] ProxyExportExecutionsRequestDto dto)
        {
            var result = await _proxyExecutionService.ExportExecutionsCsvAsync(GetTenantId(), dto);
            if (!result.IsSuccess)
            {
                return StatusCode(result.HttpStatus, new { code = result.Code, message = result.Message, errors = result.Errors });
            }

            if (result.Truncated)
            {
                Response.Headers["X-Proxy-Export-Truncated"] = "true";
            }

            return File(result.Content, "text/csv; charset=utf-8", result.FileName);
        }

        private static string GetTenantId()
        {
            var context = BlocksContext.GetContext();
            return context?.TenantId ?? string.Empty;
        }
    }
}
