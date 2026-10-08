using BlocksTemplate.Api.Security;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;

namespace XUnitTest.Api
{
    /// <summary>
    /// The SPA's CSP and security headers. Pinned: hosts follow configuration (one image runs on
    /// dev, stg and prod), no inline script is allowed, styles keep 'unsafe-inline' (genesis-os
    /// login injects &lt;style&gt;), and headers a controller already set are never replaced.
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

            policy.Should().NotContain("ws://logic.example.com");
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
        public void Policy_ForbidsInlineScriptAndEvalButAllowsWhatTheSpaLoads()
        {
            var policy = ContentSecurityPolicy.Build(Config(new()));

            var script = Directive(policy, "script-src");
            script.Should().NotContain("unsafe-inline").And.NotContain("unsafe-eval").And.NotContain("*");
            script.Should().Contain(ContentSecurityPolicy.MonacoSource);
            ContentSecurityPolicy.MonacoSource.Should().EndWith("/", "a CSP path source without '/' matches one file only");

            Directive(policy, "style-src").Should().Contain("'unsafe-inline'").And.Contain("https://fonts.googleapis.com");
            Directive(policy, "font-src").Should().Contain("https://fonts.gstatic.com");
            Directive(policy, "worker-src").Should().Contain("blob:");
            Directive(policy, "connect-src").Should().Contain("https://api.rollbar.com").And.Contain("https://*.blob.core.windows.net");
            policy.Should().Contain("frame-ancestors 'none'").And.Contain("object-src 'none'").And.Contain("base-uri 'self'");
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
        public void Apply_CachesHashedAssetsButNotRuntimeConfig(string path, string expected)
        {
            var context = Request(path);

            SecurityHeaders.Apply(context, Policy, CspMode.Enforce);

            context.Response.Headers["Cache-Control"].ToString().Should().Be(expected);
        }
    }
}
