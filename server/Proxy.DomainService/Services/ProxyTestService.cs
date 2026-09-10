using System.Text;
using Microsoft.Extensions.Logging;
using Proxy.DomainService.Dtos;
using Proxy.DomainService.Entities;
using Proxy.DomainService.Repositories;
using Proxy.DomainService.Utils;

namespace Proxy.DomainService.Services
{
    /// <summary>
    /// Implements <see cref="IProxyTestService"/>. Validation only decides <em>whether</em> to forward; the
    /// forward itself and the response shaping are delegated to <see cref="IProxyGatewayService"/> so the
    /// test path and the data-plane path can never diverge.
    /// </summary>
    public sealed class ProxyTestService : IProxyTestService
    {
        private const string DraftSlug = "(draft)";

        private readonly IProxyRepository _proxyRepository;
        private readonly IProxyGatewayService _gatewayService;
        private readonly ILogger<ProxyTestService> _logger;

        public ProxyTestService(
            IProxyRepository proxyRepository,
            IProxyGatewayService gatewayService,
            ILogger<ProxyTestService> logger)
        {
            _proxyRepository = proxyRepository;
            _gatewayService = gatewayService;
            _logger = logger;
        }

        public async Task<ProxyTestOutcome> TestAsync(string tenantId, string? userId, ProxyTestRequestDto request)
        {
            var method = (request.Method ?? string.Empty).Trim().ToUpperInvariant();
            var hasProxyId = !string.IsNullOrWhiteSpace(request.ProxyId);
            var hasDraft = request.Draft is not null;

            var errors = new Dictionary<string, string>(StringComparer.Ordinal);
            if (hasProxyId == hasDraft)
            {
                errors["request"] = "Provide exactly one of proxyId or draft.";
            }

            if (method.Length == 0)
            {
                errors["method"] = "method is required.";
            }

            if (errors.Count > 0)
            {
                _logger.LogWarning("Proxy test for tenant {TenantId} rejected: {Errors}", tenantId, string.Join("; ", errors.Values));
                return ProxyTestOutcome.Validation(errors);
            }

            ProxyResolvedConfig config;
            if (hasProxyId)
            {
                var proxy = await _proxyRepository.GetAsync(tenantId, request.ProxyId!);
                if (proxy is null)
                {
                    _logger.LogWarning("Proxy test for tenant {TenantId} rejected: proxy {ProxyId} not found.", tenantId, request.ProxyId);
                    return ProxyTestOutcome.Validation(new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["proxyId"] = $"Proxy '{request.ProxyId}' was not found.",
                    });
                }

                if (!HttpMethodTypeExtensions.TryParse(method, out var savedMethod) || !proxy.Methods.Contains(savedMethod))
                {
                    _logger.LogWarning(
                        "Proxy test for tenant {TenantId} rejected: method {Method} not enabled for proxy {ProxyId}.",
                        tenantId, method, request.ProxyId);
                    return ProxyTestOutcome.Validation(new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["method"] = $"{method} is not enabled for this proxy.",
                    });
                }

                config = ProxyResolvedConfig.FromEntity(proxy);
            }
            else
            {
                var draft = request.Draft!;
                var validation = ProxyConfigValidator.Validate(
                    "draft", draft.Upstream, draft.Methods, draft.Headers, draft.Query, draft.MethodConfigs,
                    draft.BodyMerge, draft.ResponseMode, draft.ResponseInclude);

                foreach (var pair in validation.Errors)
                {
                    if (pair.Key != "name")
                    {
                        errors[pair.Key] = pair.Value;
                    }
                }

                if (errors.Count == 0
                    && (!HttpMethodTypeExtensions.TryParse(method, out var draftMethod) || !validation.Methods.Contains(draftMethod)))
                {
                    errors["method"] = $"{method} is not one of the draft's methods.";
                }

                if (errors.Count > 0)
                {
                    _logger.LogWarning("Proxy test (draft) for tenant {TenantId} rejected: {Errors}", tenantId, string.Join("; ", errors.Values));
                    return ProxyTestOutcome.Validation(errors);
                }

                config = new ProxyResolvedConfig
                {
                    ProxyId = string.Empty,
                    Slug = DraftSlug,
                    Upstream = validation.Upstream,
                    Methods = validation.Methods,
                    Enabled = true,
                    Headers = validation.Headers,
                    Query = validation.Query,
                    BodyMerge = validation.BodyMerge,
                    MethodConfigs = validation.MethodConfigs,
                    ResponseMode = validation.ResponseMode,
                    ResponseInclude = validation.ResponseInclude,
                };
            }

            byte[]? body = string.IsNullOrEmpty(request.Body) ? null : Encoding.UTF8.GetBytes(request.Body);
            var contentType = request.ContentType;
            if (body is not null && string.IsNullOrWhiteSpace(contentType))
            {
                contentType = "application/json";
            }

            var pathSuffix = (request.PathSuffix ?? string.Empty).Trim();
            var incomingQuery = (request.Query ?? string.Empty).Trim().TrimStart('?');

            var forward = await _gatewayService.ForwardAsync(new ProxyForwardRequest
            {
                TenantId = tenantId,
                UserId = userId,
                Slug = config.Slug,
                ResolvedConfig = config,
                Method = method,
                PathSuffix = pathSuffix,
                IncomingQuery = incomingQuery,
                RequestPath = $"/api/proxy/gateway/{config.Slug}/{pathSuffix.TrimStart('/')}",
                Body = body,
                BodyTooLarge = body is not null && body.LongLength > ProxyGatewayService.MaxRequestBodyBytes,
                ContentType = contentType,
                IsTest = true,
            });

            _logger.LogInformation(
                "Proxy test for tenant {TenantId} completed: outcome {Outcome}, status {Status}, {LatencyMs} ms (no row written).",
                tenantId, forward.Outcome, forward.StatusCode, forward.LatencyMs);

            return ProxyTestOutcome.Ok(new ProxyTestResponseDto
            {
                Ok = forward.Ok,
                Status = forward.StatusCode,
                Outcome = forward.Outcome,
                LatencyMs = forward.LatencyMs,
                UpstreamUrl = forward.UpstreamUrl,
                UpstreamHost = forward.UpstreamHost,
                InjectedHeaderKeys = forward.InjectedHeaderKeys.ToList(),
                InjectedQueryKeys = forward.InjectedQueryKeys.ToList(),
                ResponseContentType = forward.ResponseContentType,
                ResponseBody = forward.ResponseBody,
                ResponseFilterApplied = forward.ResponseFilterApplied,
                ResponseFilterNote = forward.ResponseFilterNote,
                ResponseBodyBytes = forward.ResponseBodyBytes,
                ErrorMessage = forward.ErrorMessage,
            });
        }
    }
}
