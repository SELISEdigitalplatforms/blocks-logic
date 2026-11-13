using BlocksTemplate.Api.Security;
using FluentAssertions;
using Microsoft.AspNetCore.Http;

namespace XUnitTest.Api;

public class StyleNonceTests
{
    private static readonly byte[] Key = Enumerable.Range(1, 32).Select(i => (byte)i).ToArray();

    [Fact]
    public void ForIsStableForTheSameSeed()
    {
        // ZAP rule 10104 compares two responses for one page; a per-response nonce made every
        // page differ. The same browser (seed) must get the same shell.
        var nonces = new StyleNonce(Key);
        var seed = StyleNonce.NewSeed();

        nonces.For(seed).Should().Be(nonces.For(seed));
    }

    [Fact]
    public void ForDiffersBetweenSeeds()
    {
        var nonces = new StyleNonce(Key);

        nonces.For(StyleNonce.NewSeed()).Should().NotBe(nonces.For(StyleNonce.NewSeed()));
    }

    [Fact]
    public void ForDependsOnTheKey()
    {
        var seed = StyleNonce.NewSeed();

        StyleNonce.CreateWithRandomKey().For(seed).Should().NotBe(new StyleNonce(Key).For(seed));
    }

    [Fact]
    public void ForCarries128BitsAndDoesNotExposeTheSeed()
    {
        var seed = StyleNonce.NewSeed();
        var nonce = new StyleNonce(Key).For(seed);

        Convert.FromBase64String(nonce).Should().HaveCount(16);
        nonce.Should().NotContain(seed);
    }

    [Fact]
    public void NewSeedIsCookieSafeAndValid()
    {
        var seed = StyleNonce.NewSeed();

        seed.Should().HaveLength(22).And.MatchRegex("^[A-Za-z0-9_-]+$");
        StyleNonce.IsValidSeed(seed).Should().BeTrue();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("short")]
    [InlineData("aaaaaaaaaaaaaaaaaaaaa;")]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaa=")]
    [InlineData("aaaaaaaaaaa aaaaaaaaaa")]
    public void IsValidSeedRejectsAnythingNewSeedCannotProduce(string? seed)
    {
        StyleNonce.IsValidSeed(seed).Should().BeFalse();
    }

    [Fact]
    public void ForRefusesAnInvalidSeed()
    {
        var act = () => new StyleNonce(Key).For("not a seed");

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ConstructorRefusesAShortKey()
    {
        var act = () => new StyleNonce(new byte[16]);

        act.Should().Throw<ArgumentException>();
    }

    private static DefaultHttpContext Request(string path, string? seedCookie = null)
    {
        var context = new DefaultHttpContext();
        context.Request.Path = path;
        if (seedCookie is not null)
        {
            context.Request.Headers.Cookie = $"{StyleNonce.SeedCookieName}={seedCookie}";
        }
        return context;
    }

    [Fact]
    public void ForRequestWithAValidSeedCookieReusesItAndSetsNoCookie()
    {
        var nonces = new StyleNonce(Key);
        var seed = StyleNonce.NewSeed();
        var context = Request("/app/console", seed);

        nonces.ForRequest(context).Should().Be(nonces.For(seed));
        context.Response.Headers.SetCookie.Should().BeEmpty();
    }

    [Fact]
    public void ForRequestShellRouteWithoutSeedSetsAHostOnlySecureHttpOnlyCookie()
    {
        var nonces = new StyleNonce(Key);
        var context = Request("/login");

        var nonce = nonces.ForRequest(context);

        var setCookie = context.Response.Headers.SetCookie.ToString();
        setCookie.Should().StartWith(StyleNonce.SeedCookieName + "=");
        setCookie.Should().ContainEquivalentOf("path=/").And.ContainEquivalentOf("secure")
            .And.ContainEquivalentOf("httponly").And.ContainEquivalentOf("samesite=lax").And.NotContainEquivalentOf("domain=");
        var seed = setCookie.Split(';')[0].Split('=', 2)[1];
        nonces.For(seed).Should().Be(nonce);
    }

    [Fact]
    public void ForRequestInvalidSeedCookieOnShellRouteIsReplaced()
    {
        var context = Request("/", "bad;seed");

        new StyleNonce(Key).ForRequest(context);

        context.Response.Headers.SetCookie.ToString().Should().StartWith(StyleNonce.SeedCookieName + "=");
    }

    [Theory]
    [InlineData("/api/projects")]
    [InlineData("/assets/index-abc.js")]
    [InlineData("/runtime-config.js")]
    public void ForRequestNonShellPathWithoutSeedSetsNoCookie(string path)
    {
        var context = Request(path);

        new StyleNonce(Key).ForRequest(context).Should().NotBeNullOrEmpty();
        context.Response.Headers.SetCookie.Should().BeEmpty();
    }

    [Theory]
    [InlineData("/", true)]
    [InlineData("", true)]
    [InlineData(null, true)]
    [InlineData("/index.html", true)]
    [InlineData("/INDEX.HTML", true)]
    [InlineData("/app/123/environments", true)]
    [InlineData("/apiary", true)]
    [InlineData("/api", false)]
    [InlineData("/api/oidc/login", false)]
    [InlineData("/API/x", false)]
    [InlineData("/csp-nonce.js", false)]
    [InlineData("/favicon.ico", false)]
    public void MayServeShellMatchesSpaRoutesOnly(string? path, bool expected)
    {
        StyleNonce.MayServeShell(path).Should().Be(expected);
    }

    [Fact]
    public void ForRequestRefusesNullContext()
    {
        var act = () => new StyleNonce(Key).ForRequest(null!);

        act.Should().Throw<ArgumentNullException>();
    }
}
