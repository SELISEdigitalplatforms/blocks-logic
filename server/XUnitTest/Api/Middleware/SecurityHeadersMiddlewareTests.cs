using BlocksTemplate.Api.Middleware;
using FluentAssertions;
using Microsoft.AspNetCore.Http;

namespace XUnitTest.Api.Middleware;

public class SecurityHeadersMiddlewareTests
{
    [Fact]
    public async Task InvokeAsync_SetsRequiredSecurityHeaders()
    {
        var context = new DefaultHttpContext();
        context.Request.Path = "/";

        var middleware = new SecurityHeadersMiddleware(_ => Task.CompletedTask);
        await middleware.InvokeAsync(context);

        var headers = context.Response.Headers;
        headers["X-Content-Type-Options"].ToString().Should().Be("nosniff");
        headers["X-Frame-Options"].ToString().Should().Be("DENY");
        headers["Strict-Transport-Security"].ToString().Should().Contain("max-age=31536000");
        var csp = headers["Content-Security-Policy"].ToString();
        csp.Should().Contain("default-src 'self' blob:");
        csp.Should().Contain("frame-ancestors 'none'");
        csp.Should().Contain("script-src 'self' blob: https://cdn.jsdelivr.net");
        csp.Should().Contain("worker-src 'self' blob:");
        csp.Should().Contain("style-src 'self'");
        csp.Should().NotContain("unsafe-inline");
        headers["Cache-Control"].ToString().Should().Contain("no-store");
    }
}
