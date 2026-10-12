using Microsoft.OpenApi;
using Microsoft.OpenApi.Reader;
using Proxy.DomainService.Dtos;
using Proxy.DomainService.Entities;
using Proxy.DomainService.Utils;

namespace Proxy.DomainService.Services
{
    /// <summary>
    /// Turns an OpenAPI document into proxy routes.
    /// <para>
    /// Reading only: this produces a preview the caller reviews and then submits like any other route
    /// edit, so an import goes through exactly the same validation, versioning and audit as a route typed
    /// by hand. Nothing here writes.
    /// </para>
    /// <para>
    /// The parser is <c>Microsoft.OpenApi</c>, already present because Swashbuckle depends on it, so this
    /// adds no dependency.
    /// </para>
    /// </summary>
    public sealed class ProxyOpenApiImportService : IProxyOpenApiImportService
    {
        /// <summary>
        /// A spec larger than this is refused unread. Specs are pasted or fetched from a URL a caller
        /// chose, so the size has to be bounded before the parser sees it.
        /// </summary>
        public const int MaxSpecBytes = 5 * 1024 * 1024;

        /// <summary>
        /// A spec can legitimately describe hundreds of operations; a proxy should not silently become
        /// hundreds of routes. Past this the caller is told to narrow it down.
        /// </summary>
        public const int MaxOperations = 200;

        /// <summary>
        /// Named client for spec fetches (PX-8), registered in <c>AddProxyServices</c> on the guarded handler:
        /// no redirects, every connection's IP checked at connect time, no cookies.
        /// </summary>
        public const string SpecClientName = "proxy-openapi-spec";

        private static readonly TimeSpan FetchTimeout = TimeSpan.FromSeconds(15);

        private readonly IHttpClientFactory? _httpClientFactory;
        private readonly IProxyUpstreamGuard? _upstreamGuard;

        /// <param name="httpClientFactory">Only needed to fetch a specification by URL.</param>
        /// <param name="upstreamGuard">
        /// Only needed to fetch by URL, and then it is mandatory: <see cref="PreviewFromUrlAsync"/> refuses
        /// rather than fetching unguarded.
        /// </param>
        public ProxyOpenApiImportService(
            IHttpClientFactory? httpClientFactory = null, IProxyUpstreamGuard? upstreamGuard = null)
        {
            _httpClientFactory = httpClientFactory;
            _upstreamGuard = upstreamGuard;
        }

        /// <inheritdoc />
        public async Task<ProxyOpenApiPreviewDto> PreviewFromUrlAsync(
            string specUrl,
            IEnumerable<ProxyRouteConfig>? existingRoutes = null,
            CancellationToken cancellationToken = default)
        {
            var preview = new ProxyOpenApiPreviewDto();

            if (string.IsNullOrWhiteSpace(specUrl))
            {
                preview.Errors.Add("No specification URL was given.");
                return preview;
            }

            if (!Uri.TryCreate(specUrl, UriKind.Absolute, out var uri)
                || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            {
                preview.Errors.Add("The specification URL must be an absolute http:// or https:// address.");
                return preview;
            }

            // Refuse rather than fetch unguarded. A missing guard is a wiring mistake, and the safe
            // reading of it is "do not make this request".
            if (_upstreamGuard is null || _httpClientFactory is null)
            {
                preview.Errors.Add("Fetching a specification by URL is not available on this server.");
                return preview;
            }

            if (await _upstreamGuard.IsTargetBlockedAsync(specUrl, cancellationToken))
            {
                preview.Errors.Add("That specification URL is not an allowed destination.");
                return preview;
            }

            // Every failure below is reported in plain words. The raw exception text is never returned: it can
            // name an internal host and port ("connection refused 10.0.0.5:6379"), which turns this endpoint
            // into a port scanner.
            string body;
            try
            {
                var client = _httpClientFactory.CreateClient(SpecClientName);

                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(FetchTimeout);

                using var response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, timeout.Token);

                // The guarded client never follows a redirect: the next hop would be a second, unchecked request.
                if ((int)response.StatusCode is >= 300 and < 400)
                {
                    preview.Errors.Add("The specification URL redirects to another address. Paste the final URL instead.");
                    return preview;
                }

                if (!response.IsSuccessStatusCode)
                {
                    preview.Errors.Add($"The specification URL answered {(int)response.StatusCode}.");
                    return preview;
                }

                // Refused early where the server declares a length; enforced while reading either way, because a
                // declared length is a claim and a chunked reply declares none.
                if (response.Content.Headers.ContentLength is > MaxSpecBytes)
                {
                    preview.Errors.Add(TooLargeMessage);
                    return preview;
                }

                var text = await ReadCappedAsync(response.Content, timeout.Token);
                if (text is null)
                {
                    preview.Errors.Add(TooLargeMessage);
                    return preview;
                }

                body = text;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (OperationCanceledException)
            {
                preview.Errors.Add("The specification URL did not answer in time.");
                return preview;
            }
            catch (Exception ex) when (IsBlocked(ex))
            {
                preview.Errors.Add("That specification URL is not an allowed destination.");
                return preview;
            }
            catch (Exception ex) when (ex is HttpRequestException or IOException)
            {
                preview.Errors.Add("The specification could not be fetched: the address could not be reached.");
                return preview;
            }

            return Preview(body, existingRoutes);
        }

        private static string TooLargeMessage => "The specification is too large.";

        /// <summary>Reads at most <see cref="MaxSpecBytes"/>; <c>null</c> when the body is longer.</summary>
        private static async Task<string?> ReadCappedAsync(HttpContent content, CancellationToken cancellationToken)
        {
            await using var stream = await content.ReadAsStreamAsync(cancellationToken);
            using var buffer = new MemoryStream();
            var chunk = new byte[81920];
            int read;
            while ((read = await stream.ReadAsync(chunk, cancellationToken)) > 0)
            {
                if (buffer.Length + read > MaxSpecBytes)
                {
                    return null;
                }

                buffer.Write(chunk, 0, read);
            }

            return System.Text.Encoding.UTF8.GetString(buffer.GetBuffer(), 0, (int)buffer.Length);
        }

        private static bool IsBlocked(Exception ex)
        {
            for (var e = ex; e is not null; e = e.InnerException)
            {
                if (e is ProxyUpstreamBlockedException)
                {
                    return true;
                }
            }

            return false;
        }

        /// <inheritdoc />
        public ProxyOpenApiPreviewDto Preview(string specJson, IEnumerable<ProxyRouteConfig>? existingRoutes = null)
        {
            var preview = new ProxyOpenApiPreviewDto();

            if (string.IsNullOrWhiteSpace(specJson))
            {
                preview.Errors.Add("The specification is empty.");
                return preview;
            }

            if (System.Text.Encoding.UTF8.GetByteCount(specJson) > MaxSpecBytes)
            {
                preview.Errors.Add("The specification is too large.");
                return preview;
            }

            ReadResult read;
            try
            {
                // LoadExternalRefs stays off. Resolving an external reference means fetching whatever URL
                // the document names, which turns a pasted file into a request this server makes on the
                // author's behalf — an SSRF with extra steps.
                var settings = new OpenApiReaderSettings { LoadExternalRefs = false };
                settings.AddJsonReader();

                read = OpenApiModelFactory.Parse(specJson, "json", settings);
            }
            catch (Exception ex)
            {
                preview.Errors.Add($"The specification could not be parsed: {ex.Message}");
                return preview;
            }

            var document = read.Document;
            if (document is null)
            {
                preview.Errors.Add("The specification could not be parsed.");
                return preview;
            }

            // A spec with a minor flaw still imports. Hard-failing on every diagnostic would reject a
            // large share of real-world documents for something that does not affect the routes.
            foreach (var error in read.Diagnostic?.Errors ?? [])
            {
                preview.Warnings.Add(error.Message);
            }

            preview.BaseUrl = document.Servers?.FirstOrDefault()?.Url ?? string.Empty;
            if (string.IsNullOrWhiteSpace(preview.BaseUrl))
            {
                preview.Warnings.Add(
                    "The specification declares no server URL, so the upstream must be entered by hand.");
            }

            var taken = new HashSet<string>(
                (existingRoutes ?? []).Select(r => $"{r.Method.Wire()} {ProxyRoutePath.Normalize(r.Path)}"),
                StringComparer.Ordinal);

            foreach (var (template, item) in document.Paths ?? [])
            {
                foreach (var (verb, operation) in item?.Operations ?? new())
                {
                    if (preview.Operations.Count >= MaxOperations)
                    {
                        preview.Warnings.Add(
                            "Not every operation is shown; narrow the specification "
                            + "or import it in parts.");
                        return preview;
                    }

                    if (!TryMapMethod(verb, out var method))
                    {
                        preview.Warnings.Add(
                            $"{verb.Method} {template} was skipped: the gateway does not forward that method.");
                        continue;
                    }

                    var path = ProxyRoutePath.Normalize(template);
                    var key = $"{method.Wire()} {path}";

                    preview.Operations.Add(new ProxyOpenApiOperationDto
                    {
                        OperationId = operation?.OperationId ?? key,
                        Method = method.Wire(),
                        Path = path,
                        Summary = operation?.Summary ?? operation?.Description ?? string.Empty,
                        QueryParameters = NamesIn(operation, ParameterLocation.Query),
                        HeaderParameters = NamesIn(operation, ParameterLocation.Header),
                        SecurityHeaders = SecurityBindings(document, operation),

                        // Reported, never overwritten. An import that silently replaced a hand-tuned route
                        // would discard configuration the spec knows nothing about.
                        AlreadyExists = taken.Contains(key),
                    });
                }
            }

            if (preview.Operations.Count == 0 && preview.Errors.Count == 0)
            {
                preview.Errors.Add("The specification declares no operations this gateway can forward.");
            }

            return preview;
        }

        /// <inheritdoc />
        public List<ProxyRouteConfigInputDto> ToRouteInputs(
            ProxyOpenApiPreviewDto preview, IEnumerable<string> selectedOperationIds)
        {
            ArgumentNullException.ThrowIfNull(preview);

            var wanted = new HashSet<string>(selectedOperationIds ?? [], StringComparer.Ordinal);

            return preview.Operations
                .Where(o => wanted.Contains(o.OperationId) && !o.AlreadyExists)
                .Select(o => new ProxyRouteConfigInputDto
                {
                    Method = o.Method,
                    Path = o.Path,

                    // The spec's path is the upstream's own shape, so the two start identical. The point
                    // of keeping both is that the tenant can then change the client-facing one without
                    // touching what the vendor expects.
                    UpstreamPath = o.Path,

                    // Declared, never filled. A parameter's value belongs to the caller or to a config
                    // variable; a value lifted out of a specification would be an example at best and a
                    // leaked credential at worst.
                    Query = o.QueryParameters.Select(n => new ProxyKeyValueInputDto { Key = n, Value = string.Empty }).ToList(),
                    Headers = o.HeaderParameters.Concat(o.SecurityHeaders)
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .Select(n => new ProxyKeyValueInputDto { Key = n, Value = string.Empty })
                        .ToList(),
                })
                .ToList();
        }

        private static List<string> NamesIn(OpenApiOperation? operation, ParameterLocation location) =>
            (operation?.Parameters ?? [])
                .Where(p => p?.In == location && !string.IsNullOrWhiteSpace(p.Name))
                .Select(p => p.Name!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

        /// <summary>
        /// The header names the document's security schemes imply — <c>Authorization</c> for HTTP auth,
        /// the scheme's own name for an API key in a header.
        /// <para>
        /// Only the names. A security scheme never carries a usable value, and anything that looked like
        /// one in a specification would be an example someone pasted, so binding it would be wrong even
        /// when it works.
        /// </para>
        /// </summary>
        private static List<string> SecurityBindings(OpenApiDocument document, OpenApiOperation? operation)
        {
            var names = new List<string>();
            var requirements = (operation?.Security?.Count ?? 0) > 0 ? operation!.Security : document.Security;

            foreach (var requirement in requirements ?? [])
            {
                if (requirement is null) continue;

                // The keys are scheme *references*; in this version they proxy the target's own Type,
                // In and Name, so no separate component lookup is needed.
                foreach (var scheme in requirement.Keys)
                {
                    if (scheme is null) continue;

                    if (scheme.Type == SecuritySchemeType.ApiKey && scheme.In == ParameterLocation.Header
                        && !string.IsNullOrWhiteSpace(scheme.Name))
                    {
                        names.Add(scheme.Name!);
                    }
                    else if (scheme.Type is SecuritySchemeType.Http or SecuritySchemeType.OAuth2
                             or SecuritySchemeType.OpenIdConnect)
                    {
                        names.Add("Authorization");
                    }
                }
            }

            return names.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        /// <summary>
        /// The gateway forwards five verbs; a specification may describe more (TRACE, OPTIONS, HEAD).
        /// Those are reported as skipped rather than silently dropped.
        /// </summary>
        private static bool TryMapMethod(System.Net.Http.HttpMethod verb, out HttpMethodType method)
        {
            method = default;
            if (verb is null) return false;

            return HttpMethodTypeExtensions.TryParse(verb.Method, out method)
                && Enum.IsDefined(method);
        }
    }

    /// <summary>Reads an OpenAPI document and proposes routes from it.</summary>
    public interface IProxyOpenApiImportService
    {
        /// <summary>
        /// Fetches a specification the caller named by URL, then previews it.
        /// <para>
        /// The URL goes through the same upstream guard as any forward. Without it this endpoint is a
        /// request this server makes to wherever a caller points it — including at a cloud metadata
        /// address, from inside the network, authenticated as us.
        /// </para>
        /// </summary>
        Task<ProxyOpenApiPreviewDto> PreviewFromUrlAsync(
            string specUrl,
            IEnumerable<ProxyRouteConfig>? existingRoutes = null,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Parses the document and lists what could be imported. Never throws on a bad specification —
        /// the reasons come back on the preview so the caller can show them.
        /// </summary>
        ProxyOpenApiPreviewDto Preview(string specJson, IEnumerable<ProxyRouteConfig>? existingRoutes = null);

        /// <summary>
        /// Turns the chosen operations into route inputs, ready to go through the ordinary route
        /// validation rather than around it.
        /// </summary>
        List<ProxyRouteConfigInputDto> ToRouteInputs(
            ProxyOpenApiPreviewDto preview, IEnumerable<string> selectedOperationIds);
    }
}
