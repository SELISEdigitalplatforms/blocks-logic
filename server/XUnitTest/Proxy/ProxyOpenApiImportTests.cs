using System.Net;
using FluentAssertions;
using Proxy.DomainService.Entities;
using Proxy.DomainService.Services;

namespace XUnitTest.Proxy
{
    /// <summary>
    /// Importing routes from an OpenAPI document.
    /// <para>
    /// The import only ever proposes: the caller reviews a preview and submits it like any other route
    /// edit, so it goes through the ordinary validation, versioning and audit rather than around them.
    /// What these cases pin is that it proposes the right things, and refuses to carry anything across
    /// that it should not.
    /// </para>
    /// </summary>
    public class ProxyOpenApiImportTests
    {
        private static readonly ProxyOpenApiImportService Service = new();

        private const string Spec = """
        {
          "openapi": "3.0.0",
          "info": { "title": "Vendor", "version": "1.0" },
          "servers": [ { "url": "https://api.vendor.com/v1" } ],
          "components": { "securitySchemes": {
              "key": { "type": "apiKey", "in": "header", "name": "X-Api-Key" } } },
          "security": [ { "key": [] } ],
          "paths": {
            "/orders/{id}": {
              "get": { "operationId": "getOrder", "summary": "Fetch one order",
                "parameters": [
                  { "name": "id", "in": "path", "required": true, "schema": { "type": "string" } },
                  { "name": "expand", "in": "query", "schema": { "type": "string" } },
                  { "name": "X-Trace", "in": "header", "schema": { "type": "string" } } ] },
              "delete": { "operationId": "deleteOrder" }
            },
            "/health": { "get": { "operationId": "health" }, "trace": { "operationId": "traced" } }
          }
        }
        """;

        [Fact]
        public void The_server_url_becomes_the_upstream()
        {
            Service.Preview(Spec).BaseUrl.Should().Be("https://api.vendor.com/v1");
        }

        [Fact]
        public void Each_operation_becomes_a_proposed_route()
        {
            var preview = Service.Preview(Spec);

            preview.Errors.Should().BeEmpty();
            preview.Operations.Select(o => $"{o.Method} {o.Path}")
                .Should().Contain(["GET orders/{id}", "DELETE orders/{id}", "GET health"]);
        }

        [Fact]
        public void A_verb_the_gateway_cannot_forward_is_reported_not_silently_dropped()
        {
            // TRACE is in the document; the gateway forwards five verbs. Saying so is the difference
            // between "the import missed something" and "the import told me why".
            var preview = Service.Preview(Spec);

            preview.Operations.Should().NotContain(o => o.Method == "TRACE");
            preview.Warnings.Should().Contain(w => w.Contains("TRACE", StringComparison.OrdinalIgnoreCase));
        }

        [Fact]
        public void Query_and_header_parameters_are_carried_over_but_path_parameters_are_not()
        {
            // A path parameter is part of the URL template, not configuration to inject.
            var order = Service.Preview(Spec).Operations.Single(o => o.OperationId == "getOrder");

            order.QueryParameters.Should().Equal("expand");
            order.HeaderParameters.Should().Equal("X-Trace");
            order.Path.Should().Be("orders/{id}", "the template is preserved, parameters and all");
        }

        [Fact]
        public void A_security_scheme_arrives_as_a_header_name_and_nothing_else()
        {
            var order = Service.Preview(Spec).Operations.Single(o => o.OperationId == "getOrder");

            order.SecurityHeaders.Should().Equal("X-Api-Key");
        }

        [Fact]
        public void Imported_rows_never_carry_a_value()
        {
            // The whole point of the proxy is that the credential lives in a config variable, not in a
            // file someone pasted. An imported value would at best be an example and at worst a leak.
            var preview = Service.Preview(Spec);
            var routes = Service.ToRouteInputs(preview, preview.Operations.Select(o => o.OperationId));

            routes.SelectMany(r => (r.Headers ?? []).Concat(r.Query ?? []))
                .Should().OnlyContain(kv => kv.Value == string.Empty);
        }

        [Fact]
        public void The_client_facing_path_and_the_upstream_path_start_identical()
        {
            var preview = Service.Preview(Spec);
            var route = Service.ToRouteInputs(preview, ["getOrder"]).Single();

            route.Path.Should().Be("orders/{id}");
            route.UpstreamPath.Should().Be("orders/{id}");
        }

        [Fact]
        public void Only_the_selected_operations_are_turned_into_routes()
        {
            var preview = Service.Preview(Spec);

            Service.ToRouteInputs(preview, ["getOrder"]).Should().ContainSingle()
                .Which.Method.Should().Be("GET");
        }

        [Fact]
        public void An_existing_route_is_flagged_and_never_overwritten()
        {
            // An import that replaced a hand-tuned route would discard configuration the document knows
            // nothing about — the injected credential, the response projection.
            var existing = new[]
            {
                new ProxyRouteConfig { Method = HttpMethodType.Get, Path = "orders/{id}" },
            };

            var preview = Service.Preview(Spec, existing);
            var clash = preview.Operations.Single(o => o.OperationId == "getOrder");

            clash.AlreadyExists.Should().BeTrue();
            Service.ToRouteInputs(preview, ["getOrder"]).Should().BeEmpty();
        }

        // ---- refusals ------------------------------------------------------------------

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        public void An_empty_specification_is_refused(string spec)
        {
            Service.Preview(spec).Errors.Should().NotBeEmpty();
        }

        [Fact]
        public void Something_that_is_not_a_specification_is_refused_without_throwing()
        {
            // Reached by pasting the wrong thing, which is common. It must come back as an error on the
            // preview, not as an exception the caller has to catch.
            var preview = Service.Preview("{ \"not\": \"a spec\" }");

            preview.Errors.Should().NotBeEmpty();
            preview.Operations.Should().BeEmpty();
        }

        [Fact]
        public void Garbage_is_refused_without_throwing()
        {
            Service.Preview("}{ this is not json").Errors.Should().NotBeEmpty();
        }

        [Fact]
        public void A_specification_over_the_size_limit_is_refused_unread()
        {
            var huge = new string('x', ProxyOpenApiImportService.MaxSpecBytes + 1);

            Service.Preview(huge).Errors.Should().Contain(e => e.Contains("MB", StringComparison.Ordinal));
        }

        // ---- fetching by URL ------------------------------------------------------------

        private sealed class BlockingGuard : global::Proxy.DomainService.Utils.IProxyUpstreamGuard
        {
            public Task<bool> IsTargetBlockedAsync(string url, CancellationToken cancellationToken = default)
                => Task.FromResult(true);
        }

        [Fact]
        public async Task A_url_the_guard_refuses_is_never_fetched()
        {
            // The guard is what stops this endpoint becoming a request this server makes to wherever a
            // caller points it — a metadata address, something inside the network.
            var service = new ProxyOpenApiImportService(
                new System.Net.Http.HttpClientFactoryStub(), new BlockingGuard());

            var preview = await service.PreviewFromUrlAsync("http://169.254.169.254/latest/meta-data/");

            preview.Errors.Should().Contain(e => e.Contains("not an allowed destination", StringComparison.Ordinal));
            preview.Operations.Should().BeEmpty();
        }

        [Fact]
        public async Task Without_a_guard_wired_up_it_refuses_rather_than_fetching()
        {
            // A missing guard is a wiring mistake. The safe reading of it is "do not make this request".
            var preview = await new ProxyOpenApiImportService().PreviewFromUrlAsync("https://example.com/spec.json");

            preview.Errors.Should().NotBeEmpty();
        }

        [Theory]
        [InlineData("not-a-url")]
        [InlineData("file:///etc/passwd")]
        [InlineData("ftp://example.com/spec.json")]
        public async Task Only_http_urls_are_accepted(string url)
        {
            var preview = await new ProxyOpenApiImportService().PreviewFromUrlAsync(url);

            preview.Errors.Should().Contain(e => e.Contains("http", StringComparison.OrdinalIgnoreCase));
        }

        [Fact]
        public void A_proxy_level_resilience_config_round_trips_through_validation()
        {
            // The proxy-wide setting is what a route inherits when it declares none.
            var result = global::Proxy.DomainService.Utils.ProxyConfigValidator.Validate(
                "Vendor API", "https://api.vendor.com", new[] { "GET" }, null, null,
                resilience: new global::Proxy.DomainService.Dtos.ProxyResilienceInputDto { TimeoutSeconds = 12 });

            result.IsValid.Should().BeTrue();
            result.Resilience!.TimeoutSeconds.Should().Be(12);
        }

        [Fact]
        public void A_proxy_with_no_resilience_stores_none()
        {
            var result = global::Proxy.DomainService.Utils.ProxyConfigValidator.Validate(
                "Vendor API", "https://api.vendor.com", new[] { "GET" }, null, null);

            result.IsValid.Should().BeTrue();
            result.Resilience.Should().BeNull();
        }

        [Fact]
        public void A_specification_with_no_server_still_imports_but_says_so()
        {
            const string noServer = """
            { "openapi": "3.0.0", "info": { "title": "x", "version": "1" },
              "paths": { "/a": { "get": { "operationId": "a" } } } }
            """;

            var preview = Service.Preview(noServer);

            preview.Operations.Should().ContainSingle();
            preview.BaseUrl.Should().BeEmpty();
            preview.Warnings.Should().Contain(w => w.Contains("server", StringComparison.OrdinalIgnoreCase));
        }
    
        // ---- fetching by URL through the guarded client (PX-8) -------------------------------

        private sealed class AllowingGuard : global::Proxy.DomainService.Utils.IProxyUpstreamGuard
        {
            public Task<bool> IsTargetBlockedAsync(string url, CancellationToken cancellationToken = default)
                => Task.FromResult(false);
        }

        private sealed class ScriptedFactory(Func<HttpRequestMessage, HttpResponseMessage> respond) : IHttpClientFactory
        {
            public string? LastName { get; private set; }
            public int Calls { get; private set; }

            public HttpClient CreateClient(string name)
            {
                LastName = name;
                return new HttpClient(new Handler(this, respond));
            }

            private sealed class Handler(ScriptedFactory owner, Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
            {
                protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
                {
                    owner.Calls++;
                    return Task.FromResult(respond(request));
                }
            }
        }

        /// <summary>A body with no Content-Length (like a chunked reply) that never ends on its own.</summary>
        private sealed class EndlessStream : Stream
        {
            public override bool CanRead => true;
            public override bool CanSeek => false;
            public override bool CanWrite => false;
            public override long Length => throw new NotSupportedException();
            public override long Position { get => 0; set => throw new NotSupportedException(); }
            public override void Flush() { }
            public override int Read(byte[] buffer, int offset, int count) { Array.Fill(buffer, (byte)'x', offset, count); return count; }
            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
            public override void SetLength(long value) => throw new NotSupportedException();
            public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        }

        private static ProxyOpenApiImportService Fetching(ScriptedFactory factory) => new(factory, new AllowingGuard());

        [Fact]
        public async Task A_good_url_is_fetched_through_the_guarded_named_client()
        {
            var factory = new ScriptedFactory(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Spec) });

            var preview = await Fetching(factory).PreviewFromUrlAsync("https://vendor.example.com/openapi.json");

            factory.LastName.Should().Be(ProxyOpenApiImportService.SpecClientName);
            preview.Errors.Should().BeEmpty();
            preview.Operations.Should().NotBeEmpty();
        }

        [Fact]
        public async Task A_redirect_is_reported_and_never_followed()
        {
            var factory = new ScriptedFactory(_ =>
            {
                var r = new HttpResponseMessage(HttpStatusCode.Found);
                r.Headers.Location = new Uri("http://169.254.169.254/latest/meta-data/");
                return r;
            });

            var preview = await Fetching(factory).PreviewFromUrlAsync("https://evil.example.com/spec");

            factory.Calls.Should().Be(1);
            preview.Errors.Should().ContainSingle().Which.Should().Contain("redirects");
            preview.Operations.Should().BeEmpty();
        }

        [Fact]
        public async Task A_body_with_no_length_is_cut_off_at_the_limit()
        {
            var factory = new ScriptedFactory(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new EndlessStream()) });

            var preview = await Fetching(factory).PreviewFromUrlAsync("https://big.example.com/spec");

            preview.Errors.Should().ContainSingle().Which.Should().Contain("MB limit");
        }

        [Fact]
        public async Task A_connect_time_block_reads_as_not_allowed()
        {
            var factory = new ScriptedFactory(_ => throw new HttpRequestException(
                "refused", new global::Proxy.DomainService.Utils.ProxyUpstreamBlockedException()));

            var preview = await Fetching(factory).PreviewFromUrlAsync("https://rebind.example.com/spec");

            preview.Errors.Should().ContainSingle().Which.Should().Contain("not an allowed destination");
        }

        [Fact]
        public async Task A_connection_error_never_echoes_the_internal_address()
        {
            var factory = new ScriptedFactory(_ => throw new HttpRequestException("Connection refused (10.0.0.5:6379)"));

            var preview = await Fetching(factory).PreviewFromUrlAsync("https://down.example.com/spec");

            preview.Errors.Should().ContainSingle()
                .Which.Should().NotContain("10.0.0.5").And.NotContain("6379").And.Contain("could not be reached");
        }
    }
}
