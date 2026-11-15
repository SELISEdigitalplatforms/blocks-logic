using BlocksTemplate.Api.Middleware;
using BlocksTemplate.Api.Security;
using FluentAssertions;
using Microsoft.AspNetCore.Http;

namespace XUnitTest.Api;

/// <summary>
/// The SPA shell is rendered with the response's style nonce, for "/" and as the fallback of
/// non-API routes, GET and HEAD only; API routes, files and answered requests pass through.
/// </summary>
public class SpaShellTests
{
    private const string Template =
        "<head><meta name=\"csp-nonce\" nonce=\"" + ContentSecurityPolicy.StyleNoncePlaceholder + "\" /></head>";

    private const string Rendered = "<head><meta name=\"csp-nonce\" nonce=\"n0nce==\" /></head>";

    private static DefaultHttpContext Context(string method, string path)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = method;
        context.Request.Path = path;
        context.Items[StyleNonce.ItemKey] = "n0nce==";
        context.Response.Body = new MemoryStream();
        return context;
    }

    private static string Body(HttpContext context)
    {
        context.Response.Body.Position = 0;
        using var reader = new StreamReader(context.Response.Body, leaveOpen: true);
        return reader.ReadToEnd();
    }

    private static RequestDelegate Answer(int status, string? body = null) => async context =>
    {
        context.Response.StatusCode = status;
        if (body is not null) await context.Response.WriteAsync(body).ConfigureAwait(false);
    };

    [Theory]
    [InlineData("GET", "/", true)]
    [InlineData("HEAD", "/", true)]
    [InlineData("GET", "", true)]
    [InlineData("GET", "/index.html", true)]
    [InlineData("GET", "/INDEX.HTML", true)]
    [InlineData("POST", "/", false)]
    [InlineData("GET", "/app/console", false)]
    [InlineData("GET", "/assets/index.js", false)]
    public void IsRootRequestMatchesOnlyReadsOfTheShell(string method, string path, bool expected)
    {
        SpaShell.IsRootRequest(Context(method, path).Request).Should().Be(expected);
    }

    [Fact]
    public void IsRootRequestRefusesNull()
    {
        FluentActions.Invoking(() => SpaShell.IsRootRequest(null!)).Should().Throw<ArgumentNullException>();
    }

    [Theory]
    [InlineData("/assets/index-abc.js", true)]
    [InlineData("/monaco/vs/loader.js", true)]
    [InlineData("/app/v1.2/x", false)]
    [InlineData("/app/console", false)]
    [InlineData("/", false)]
    [InlineData("", false)]
    public void LooksLikeAFileChecksTheLastSegment(string path, bool expected)
    {
        SpaShell.LooksLikeAFile(new PathString(string.IsNullOrEmpty(path) ? null : path)).Should().Be(expected);
    }

    [Theory]
    [InlineData("GET", "/")]
    [InlineData("GET", "/index.html")]
    [InlineData("HEAD", "/")]
    public async Task InvokeAsyncRendersTheRootWithoutCallingTheNextMiddleware(string method, string path)
    {
        var context = Context(method, path);
        var nextCalled = false;

        await SpaShell.InvokeAsync(context, _ => { nextCalled = true; return Task.CompletedTask; }, Template);

        nextCalled.Should().BeFalse();
        context.Response.ContentType.Should().Be("text/html; charset=utf-8");
        Body(context).Should().Be(Rendered);
    }

    [Theory]
    [InlineData("GET", "/app/console")]
    [InlineData("HEAD", "/app/123/dashboard")]
    [InlineData("GET", "/login")]
    public async Task InvokeAsyncRendersTheShellForUnansweredSpaRoutes(string method, string path)
    {
        var context = Context(method, path);
        context.Response.Headers["X-Stale"] = "1";

        await SpaShell.InvokeAsync(context, Answer(StatusCodes.Status404NotFound), Template);

        context.Response.StatusCode.Should().Be(StatusCodes.Status200OK);
        context.Response.ContentType.Should().Be("text/html; charset=utf-8");
        context.Response.Headers.ContainsKey("X-Stale").Should().BeFalse();
        Body(context).Should().Be(Rendered);
    }

    [Theory]
    [InlineData("GET", "/api/workflow/missing")]
    [InlineData("GET", "/API")]
    [InlineData("GET", "/assets/missing-chunk.js")]
    [InlineData("POST", "/app/console")]
    [InlineData("PUT", "/app/console")]
    public async Task InvokeAsyncLeavesApiFilesAndOtherMethodsAtTheir404(string method, string path)
    {
        var context = Context(method, path);

        await SpaShell.InvokeAsync(context, Answer(StatusCodes.Status404NotFound, "not found"), Template);

        context.Response.StatusCode.Should().Be(StatusCodes.Status404NotFound);
        Body(context).Should().Be("not found");
    }

    [Fact]
    public async Task InvokeAsyncKeepsWhatTheNextMiddlewareAnswered()
    {
        var context = Context("GET", "/app/console");

        await SpaShell.InvokeAsync(context, Answer(StatusCodes.Status200OK, "ok"), Template);

        Body(context).Should().Be("ok");
    }

    [Fact]
    public async Task InvokeAsyncRefusesMissingArguments()
    {
        await FluentActions.Awaiting(() => SpaShell.InvokeAsync(null!, Answer(200), Template))
            .Should().ThrowAsync<ArgumentNullException>();
        await FluentActions.Awaiting(() => SpaShell.InvokeAsync(Context("GET", "/"), null!, Template))
            .Should().ThrowAsync<ArgumentNullException>();
        await FluentActions.Awaiting(() => SpaShell.WriteAsync(null!, Template))
            .Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task ReadTemplateAsyncReadsIndexHtmlOrReturnsNull()
    {
        var root = Path.Combine(Path.GetTempPath(), "spa-shell-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            (await SpaShell.ReadTemplateAsync(root)).Should().BeNull();
            (await SpaShell.ReadTemplateAsync(null)).Should().BeNull();

            await File.WriteAllTextAsync(Path.Combine(root, "index.html"), Template);
            (await SpaShell.ReadTemplateAsync(root)).Should().Be(Template);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
