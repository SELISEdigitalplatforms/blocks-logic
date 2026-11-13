using BlocksTemplate.Api.Security;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;

namespace XUnitTest.Api
{
    /// <summary>
    /// The SPA's CSP and security headers. Pinned: hosts follow configuration (one image runs on
    /// dev, stg and prod), no inline script is allowed, style elements need the style nonce
    /// (genesis-os login injects &lt;style&gt;, stamped by csp-nonce.js), and headers a
    /// controller already set are never replaced.
    /// </summary>
    public class SecurityHeadersTests
    {
        private const string Policy = "default-src 'self'";

        private static IConfiguration Config(Dictionary<string, string?> values) =>
            new ConfigurationBuilder().AddInMemoryCollection(values).Build();

        private static DefaultHttpContext Request(string path)
        {
            var context = new DefaultHttpContext();
            context.Request.Path = path;
            return context;
        }

        private static string Directive(string policy, string name) =>
            policy.Split(';', StringSplitOptions.TrimEntries)
                .Single(d => d.StartsWith(name + " ", StringComparison.Ordinal));

        // ---------- Policy ----------

        [Fact]
        public void Build_UsesTheConfiguredEnvironmentsHosts()
        {
            var policy = ContentSecurityPolicy.Build(Config(new()
            {
                ["FrontendRuntime:BLOCKS_IAM_BASE_URL"] = "https://iam.example.com/some/path?x=1",
                ["FrontendRuntime:BLOCKS_LOGIC_BASE_URL"] = "https://logic.example.com/",
                ["FrontendRuntime:BLOCKS_OS_BASE_URL"] = "https://os.example.com",
            }));

            var connect = Directive(policy, "connect-src");
            connect.Should().Contain("https://iam.example.com")
                .And.Contain("https://logic.example.com")
                .And.Contain("wss://logic.example.com")
                .And.NotContain("/some/path");
            Directive(policy, "form-action").Should()
                .Be("form-action 'self' https://iam.example.com https://os.example.com");
            policy.Should().NotContain("dev-");
        }

        [Theory]
        [InlineData("__BLOCKS_IAM_BASE_URL__")]
        [InlineData("https://evil.example.com; script-src *")]
        [InlineData("javascript:alert(1)")]
        [InlineData("   ")]
        public void Build_DropsValuesThatAreNotOrigins(string value)
        {
            var policy = ContentSecurityPolicy.Build(Config(new() { ["FrontendRuntime:BLOCKS_IAM_BASE_URL"] = value }));

            policy.Should().NotContain("__BLOCKS_").And.NotContain("javascript:").And.NotContain("script-src *");
        }

        [Fact]
        public void Build_PlainHttpLogicHostGetsNoWebSocketAllowance()
        {
            var policy = ContentSecurityPolicy.Build(Config(new() { ["FrontendRuntime:BLOCKS_LOGIC_BASE_URL"] = "http://logic.example.com" }));

            // Uri.UriSchemeWs ("ws"): the plain-WebSocket origin that must NOT be allowed for a plain-HTTP host.
            policy.Should().NotContain($"{Uri.UriSchemeWs}://logic.example.com");
        }

        [Fact]
        public void Build_AddsExtraSourcesFromCspSection()
        {
            var policy = ContentSecurityPolicy.Build(Config(new()
            {
                ["Csp:ExtraConnectSrc"] = "https://a.example.com, https://b.example.com",
                ["Csp:ExtraFormAction"] = "https://login.example.com",
            }));

            Directive(policy, "connect-src").Should().Contain("https://a.example.com").And.Contain("https://b.example.com");
            Directive(policy, "form-action").Should().Contain("https://login.example.com");
        }

        [Fact]
        public void Policy_ForbidsInlineScriptAndEvalAndThirdPartyScripts()
        {
            var policy = ContentSecurityPolicy.Build(Config(new()));

            // Monaco is self-hosted (/monaco/vs) and reCAPTCHA is not used: no script host at all.
            Directive(policy, "script-src").Should().Be("script-src 'self'");
            policy.Should().NotContain("jsdelivr").And.NotContain("recaptcha").And.NotContain("frame-src");

            Directive(policy, "style-src").Should().Be($"style-src 'self' 'nonce-{ContentSecurityPolicy.StyleNoncePlaceholder}'");
            Directive(policy, "font-src").Should().Be("font-src 'self' data:");
            Directive(policy, "worker-src").Should().Contain("blob:");
            Directive(policy, "connect-src").Should().Contain("https://api.rollbar.com");
            policy.Should().Contain("frame-ancestors 'none'").And.Contain("object-src 'none'").And.Contain("base-uri 'self'");
        }

        [Fact]
        public void Policy_ImagesAreLimitedToSelfAndStorage()
        {
            var policy = ContentSecurityPolicy.Build(Config(new() { ["Csp:ExtraImgSrc"] = "https://cdn.example.com" }));

            var img = Directive(policy, "img-src");
            img.Should().StartWith("img-src 'self' data: blob:").And.Contain("https://cdn.example.com");
            img.Split(' ').Should().NotContain("https:", "any https host is what the scan flags");
        }

        [Fact]
        public void Policy_UnconfiguredBlobStorageKeepsTheWildcardSoImportStillWorks()
        {
            var policy = ContentSecurityPolicy.Build(Config(new()));

            Directive(policy, "connect-src").Should().Contain(ContentSecurityPolicy.BlobStorageFallback);
            Directive(policy, "img-src").Should().Contain(ContentSecurityPolicy.BlobStorageFallback);
        }

        [Fact]
        public void Policy_ConfiguredBlobStorageReplacesTheWildcard()
        {
            var policy = ContentSecurityPolicy.Build(Config(new()
            {
                ["Csp:BlobStorageSrc"] = "https://blocksdev.blob.core.windows.net/container?sv=x, not-a-url",
            }));

            policy.Should().NotContain("*.blob.core.windows.net").And.NotContain("sv=x").And.NotContain("not-a-url");
            Directive(policy, "connect-src").Should().Contain("https://blocksdev.blob.core.windows.net");
            Directive(policy, "img-src").Should().Contain("https://blocksdev.blob.core.windows.net");
        }

        [Fact]
        public void Policy_ReportsViolations()
        {
            var policy = ContentSecurityPolicy.Build(Config(new()));

            Directive(policy, "report-to").Should().Be("report-to " + ContentSecurityPolicy.ReportGroup);
            Directive(policy, "report-uri").Should().Be("report-uri " + ContentSecurityPolicy.ReportPath);
        }

        [Fact]
        public void PolicyAllowsStyleElementsOnlyWithTheNonceAndKeepsStyleAttributes()
        {
            var policy = ContentSecurityPolicy.Build(Config(new()));

            // ZAP 10055: no 'unsafe-inline' for style elements; attributes cannot carry a nonce.
            Directive(policy, "style-src-attr").Should().Be("style-src-attr 'unsafe-inline'");
            Directive(policy, "style-src").Should().NotContain("'unsafe-inline'");
            // ZAP 90003: no third-party stylesheet or font host left to load without SRI.
            policy.Should().NotContain("fonts.googleapis.com").And.NotContain("fonts.gstatic.com");
            Directive(policy, "worker-src").Should().Be("worker-src 'self' blob:");
        }

        [Fact]
        public void WithStyleNonceReplacesThePlaceholder()
        {
            var policy = ContentSecurityPolicy.WithStyleNonce(ContentSecurityPolicy.Build(Config(new())), "abc123==");

            Directive(policy, "style-src").Should().Be("style-src 'self' 'nonce-abc123=='");
            policy.Should().NotContain(ContentSecurityPolicy.StyleNoncePlaceholder);
        }

        [Fact]
        public void RenderIndexWritesTheNonceIntoTheShell()
        {
            var template = $"<meta name=\"csp-nonce\" nonce=\"{ContentSecurityPolicy.StyleNoncePlaceholder}\" />";

            ContentSecurityPolicy.RenderIndex(template, "abc123==").Should()
                .Be("<meta name=\"csp-nonce\" nonce=\"abc123==\" />");
        }

        [Fact]
        public void WithStyleNonceAndRenderIndexRefuseMissingValues()
        {
            FluentActions.Invoking(() => ContentSecurityPolicy.WithStyleNonce(null!, "n")).Should().Throw<ArgumentNullException>();
            FluentActions.Invoking(() => ContentSecurityPolicy.WithStyleNonce("p", "")).Should().Throw<ArgumentException>();
            FluentActions.Invoking(() => ContentSecurityPolicy.RenderIndex(null!, "n")).Should().Throw<ArgumentNullException>();
            FluentActions.Invoking(() => ContentSecurityPolicy.RenderIndex("t", null!)).Should().Throw<ArgumentException>();
        }

        // ---------- Mode ----------

        [Theory]
        [InlineData(null, CspMode.Enforce)]
        [InlineData("", CspMode.Enforce)]
        [InlineData("enforce", CspMode.Enforce)]
        [InlineData("ReportOnly", CspMode.ReportOnly)]
        [InlineData("off", CspMode.Off)]
        [InlineData("Of", CspMode.Enforce)]
        [InlineData("2", CspMode.Off)]
        [InlineData("7", CspMode.Enforce)]
        public void ReadMode_UnknownValuesEnforce(string? value, CspMode expected)
        {
            SecurityHeaders.ReadMode(Config(new() { ["Csp:Mode"] = value })).Should().Be(expected);
        }

        // ---------- Headers ----------

        [Theory]
        [InlineData("/")]
        [InlineData("/workflows/123")]
        [InlineData("/index.html")]
        public void Apply_PageGetsTheFullSet(string path)
        {
            var context = Request(path);

            SecurityHeaders.Apply(context, Policy, CspMode.Enforce);

            var headers = context.Response.Headers;
            headers["Content-Security-Policy"].ToString().Should().Be(Policy);
            headers["X-Content-Type-Options"].ToString().Should().Be("nosniff");
            headers["X-Frame-Options"].ToString().Should().Be("DENY");
            headers["Strict-Transport-Security"].ToString().Should().StartWith("max-age=");
            headers["Referrer-Policy"].ToString().Should().NotBeEmpty();
            headers["Permissions-Policy"].ToString().Should().NotBeEmpty();
            headers["Cache-Control"].ToString().Should().Be("no-cache");
            headers["Reporting-Endpoints"].ToString().Should()
                .Be($"{ContentSecurityPolicy.ReportGroup}=\"{ContentSecurityPolicy.ReportPath}\"");
        }

        [Fact]
        public void Apply_ReportOnlyBlocksNothing()
        {
            var context = Request("/");

            SecurityHeaders.Apply(context, Policy, CspMode.ReportOnly);

            context.Response.Headers.ContainsKey("Content-Security-Policy").Should().BeFalse();
            context.Response.Headers["Content-Security-Policy-Report-Only"].ToString().Should().Be(Policy);
        }

        [Fact]
        public void Apply_OffSendsNoCspButKeepsOtherHeaders()
        {
            var context = Request("/");

            SecurityHeaders.Apply(context, Policy, CspMode.Off);

            context.Response.Headers.ContainsKey("Content-Security-Policy").Should().BeFalse();
            context.Response.Headers.ContainsKey("Content-Security-Policy-Report-Only").Should().BeFalse();
            context.Response.Headers.ContainsKey("Reporting-Endpoints").Should().BeFalse();
            context.Response.Headers["X-Frame-Options"].ToString().Should().Be("DENY");
        }

        [Theory]
        [InlineData("/api/fn/abc")]
        [InlineData("/api/proxy/x/y")]
        [InlineData("/API/Workflow/Get")]
        [InlineData("/swagger/index.html")]
        public void Apply_ApiAndSwaggerGetNoSpaPolicyOrFrameBan(string path)
        {
            var context = Request(path);

            SecurityHeaders.Apply(context, Policy, CspMode.Enforce);

            var headers = context.Response.Headers;
            headers.ContainsKey("Content-Security-Policy").Should().BeFalse();
            headers.ContainsKey("X-Frame-Options").Should().BeFalse();
            headers.ContainsKey("Cache-Control").Should().BeFalse();
            headers["X-Content-Type-Options"].ToString().Should().Be("nosniff");
        }

        [Fact]
        public void Apply_NeverReplacesHeadersTheResponseAlreadySet()
        {
            var context = Request("/");
            context.Response.Headers["Content-Security-Policy"] = "sandbox";
            context.Response.Headers["Cache-Control"] = "private, max-age=60";
            context.Response.Headers["X-Frame-Options"] = "SAMEORIGIN";

            SecurityHeaders.Apply(context, Policy, CspMode.Enforce);

            context.Response.Headers["Content-Security-Policy"].ToString().Should().Be("sandbox");
            context.Response.Headers["Cache-Control"].ToString().Should().Be("private, max-age=60");
            context.Response.Headers["X-Frame-Options"].ToString().Should().Be("SAMEORIGIN");
        }

        [Fact]
        public void Apply_ExistingReportOnlyPolicyIsNotDoubled()
        {
            var context = Request("/");
            context.Response.Headers["Content-Security-Policy-Report-Only"] = "default-src 'none'";

            SecurityHeaders.Apply(context, Policy, CspMode.Enforce);

            context.Response.Headers.ContainsKey("Content-Security-Policy").Should().BeFalse();
        }

        [Theory]
        [InlineData("/assets/index-Dl5BJBN8.js", "public, max-age=31536000, immutable")]
        [InlineData("/runtime-config.js", "no-cache")]
        [InlineData("/csp-nonce.js", "no-cache")]
        public void Apply_CachesHashedAssetsButNotRuntimeConfig(string path, string expected)
        {
            var context = Request(path);

            SecurityHeaders.Apply(context, Policy, CspMode.Enforce);

            context.Response.Headers["Cache-Control"].ToString().Should().Be(expected);
        }

        // ---------- Middleware ----------

        private static readonly byte[] NonceKey = Enumerable.Range(1, 32).Select(i => (byte)i).ToArray();

        private const string NoncePolicy = "style-src 'self' 'nonce-" + ContentSecurityPolicy.StyleNoncePlaceholder + "'";

        /// <summary>DefaultHttpContext ignores OnStarting; this feature records and runs the callbacks.</summary>
        private sealed class StartingResponseFeature : Microsoft.AspNetCore.Http.Features.HttpResponseFeature
        {
            private readonly List<(Func<object, Task> Callback, object State)> _starting = [];

            public override void OnStarting(Func<object, Task> callback, object state) => _starting.Add((callback, state));

            public async Task StartAsync()
            {
                foreach (var (callback, state) in _starting) await callback(state).ConfigureAwait(false);
            }
        }

        private static (DefaultHttpContext Context, StartingResponseFeature Feature) SeededRequest(string path, string seed)
        {
            var context = Request(path);
            var feature = new StartingResponseFeature();
            context.Features.Set<Microsoft.AspNetCore.Http.Features.IHttpResponseFeature>(feature);
            context.Request.Headers.Cookie = $"{StyleNonce.SeedCookieName}={seed}";
            return (context, feature);
        }

        [Fact]
        public async Task InvokeAsyncSendsThePolicyWithTheBrowsersNonceAndKeepsItForTheShell()
        {
            var nonces = new StyleNonce(NonceKey);
            var seed = StyleNonce.NewSeed();
            var (context, feature) = SeededRequest("/app/console", seed);
            var nextCalled = false;

            await SecurityHeaders.InvokeAsync(context, _ => { nextCalled = true; return Task.CompletedTask; },
                NoncePolicy, CspMode.Enforce, nonces);
            await feature.StartAsync();

            var nonce = nonces.For(seed);
            nextCalled.Should().BeTrue();
            context.Items[StyleNonce.ItemKey].Should().Be(nonce);
            context.Response.Headers["Content-Security-Policy"].ToString().Should().Be($"style-src 'self' 'nonce-{nonce}'");
        }

        [Fact]
        public async Task InvokeAsyncGivesTheSameBrowserTheSamePolicy()
        {
            // ZAP 10104 compares two responses for one page: one browser must get one shell.
            var nonces = new StyleNonce(NonceKey);
            var seed = StyleNonce.NewSeed();
            var policies = new List<string>();
            for (var i = 0; i < 2; i++)
            {
                var (context, feature) = SeededRequest("/", seed);
                await SecurityHeaders.InvokeAsync(context, _ => Task.CompletedTask, NoncePolicy, CspMode.Enforce, nonces);
                await feature.StartAsync();
                policies.Add(context.Response.Headers["Content-Security-Policy"].ToString());
            }

            policies[0].Should().Be(policies[1]);
        }

        [Fact]
        public async Task InvokeAsyncKeepsAPolicyAControllerSet()
        {
            var (context, feature) = SeededRequest("/api/proxy/x", StyleNonce.NewSeed());

            await SecurityHeaders.InvokeAsync(context, ctx =>
            {
                ctx.Response.Headers["Content-Security-Policy"] = "sandbox";
                return Task.CompletedTask;
            }, NoncePolicy, CspMode.Enforce, new StyleNonce(NonceKey));
            await feature.StartAsync();

            context.Response.Headers["Content-Security-Policy"].ToString().Should().Be("sandbox");
        }

        [Fact]
        public async Task InvokeAsyncRefusesMissingArguments()
        {
            var nonces = new StyleNonce(NonceKey);
            RequestDelegate next = _ => Task.CompletedTask;

            await FluentActions.Awaiting(() => SecurityHeaders.InvokeAsync(null!, next, NoncePolicy, CspMode.Enforce, nonces))
                .Should().ThrowAsync<ArgumentNullException>();
            await FluentActions.Awaiting(() => SecurityHeaders.InvokeAsync(Request("/"), null!, NoncePolicy, CspMode.Enforce, nonces))
                .Should().ThrowAsync<ArgumentNullException>();
            await FluentActions.Awaiting(() => SecurityHeaders.InvokeAsync(Request("/"), next, NoncePolicy, CspMode.Enforce, null!))
                .Should().ThrowAsync<ArgumentNullException>();
        }
    }
}
