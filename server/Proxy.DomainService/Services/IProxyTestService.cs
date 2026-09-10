using Proxy.DomainService.Dtos;
using Proxy.DomainService.Utils;

namespace Proxy.DomainService.Services
{
    /// <summary>
    /// Backs the console-only <c>POST /api/Proxy/Test</c> action: validate the request (exactly one of
    /// proxyId / draft; method enabled; draft passes Phase-1 rules), then run the identical forward pipeline
    /// via <see cref="IProxyGatewayService"/> with <c>IsTest = true</c> so NO execution row is written and NO
    /// <c>Proxies</c> / <c>ProxyVersions</c> data is created or mutated.
    /// </summary>
    public interface IProxyTestService
    {
        Task<ProxyTestOutcome> TestAsync(string tenantId, string? userId, ProxyTestRequestDto request);
    }

    /// <summary>
    /// Result of <see cref="IProxyTestService.TestAsync"/>: either a 200 <see cref="ProxyTestResponseDto"/> or
    /// a 400 <c>{ code, errors }</c> validation failure (SPEC C8 / example 9).
    /// </summary>
    public sealed class ProxyTestOutcome
    {
        public bool IsSuccess { get; private init; }

        public int HttpStatus { get; private init; } = 200;

        public string? Code { get; private init; }

        public IDictionary<string, string>? Errors { get; private init; }

        public ProxyTestResponseDto? Result { get; private init; }

        public static ProxyTestOutcome Ok(ProxyTestResponseDto result) => new()
        {
            IsSuccess = true,
            HttpStatus = 200,
            Result = result,
        };

        public static ProxyTestOutcome Validation(IDictionary<string, string> errors) => new()
        {
            IsSuccess = false,
            HttpStatus = 400,
            Code = ProxyErrorCodes.Validation,
            Errors = errors,
        };
    }
}
