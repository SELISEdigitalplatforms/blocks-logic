using System.Text;
using FluentAssertions;
using Proxy.DomainService.Entities;
using Proxy.DomainService.Utils;

namespace XUnitTest.Proxy
{
    /// <summary>P-5 / PS-2 / PS-7 / PS-11 (2026-10-07): a secret is never shown — not to the caller, not in a log, not in history.</summary>
    public class ProxySecretRedactorTests
    {
        private static readonly Dictionary<string, string> NoVars = new();

        [Fact]
        public void Collects_resolved_values_and_literal_credentials_but_not_short_or_harmless_values()
        {
            var secrets = ProxySecretRedactor.CollectSecrets(
                new Dictionary<string, string> { ["k"] = "sk_live_123456", ["short"] = "abc" },
                new[]
                {
                    new ProxyKeyValue { Key = "Authorization", Value = "Bearer literal_token_9" },
                    new ProxyKeyValue { Key = "Content-Type", Value = "application/json" },
                    new ProxyKeyValue { Key = "x-api-key", Value = "{{$VAR.k}}" },
                });

            secrets.Should().Contain(new[] { "sk_live_123456", "Bearer literal_token_9", "literal_token_9" });
            secrets.Should().NotContain("abc").And.NotContain("application/json");
        }

        [Fact]
        public void Masks_text_bodies_and_leaves_binary_bodies_untouched()
        {
            var secrets = new[] { "sk_live_123456" };
            var text = Encoding.UTF8.GetBytes("bad key sk_live_123456");
            Encoding.UTF8.GetString(ProxySecretRedactor.MaskBody(text, "application/json; charset=utf-8", secrets)!)
                .Should().Be("bad key ***");

            var binary = Encoding.UTF8.GetBytes("sk_live_123456");
            ProxySecretRedactor.MaskBody(binary, "image/png", secrets).Should().BeSameAs(binary);

            var clean = Encoding.UTF8.GetBytes("{}");
            ProxySecretRedactor.MaskBody(clean, "application/json", secrets).Should().BeSameAs(clean, "nothing matched, nothing copied");
        }

        [Theory]
        [InlineData("q=1&api_key=abc", "q=1&api_key=***")]
        [InlineData("access_token=x&page=2", "access_token=***&page=2")]
        [InlineData("Signature=s&sig=t&x-blocks-key=tenant", "Signature=***&sig=***&x-blocks-key=***")]
        [InlineData("", "")]
        public void Redacts_credential_looking_query_values(string query, string expected) =>
            ProxySecretRedactor.RedactQuery(query).Should().Be(expected);

        [Fact]
        public void Redacts_user_info_and_query_in_a_url()
        {
            ProxySecretRedactor.RedactUrl("https://u:p@api.x.com/a/b?token=t&q=1")
                .Should().Be("https://api.x.com/a/b?token=***&q=1");
        }

        [Fact]
        public void History_masks_literal_credentials_and_keeps_variable_references()
        {
            ProxyChangeSet.MaskForDisplay("header:Authorization", "Bearer sk_live_1").Should().Be("***");
            ProxyChangeSet.MaskForDisplay("header:Authorization", "Bearer {{$VAR.k}}").Should().Be("Bearer {{$VAR.k}}");
            ProxyChangeSet.MaskForDisplay("query:api_key", "abc").Should().Be("***");
            ProxyChangeSet.MaskForDisplay("body:password", "pw").Should().Be("***");
            ProxyChangeSet.MaskForDisplay("method:GET:header:x-api-key", "abc").Should().Be("***");
            ProxyChangeSet.MaskForDisplay("header:Accept", "application/json").Should().Be("application/json");
            ProxyChangeSet.MaskForDisplay("upstream", "https://a.b").Should().Be("https://a.b");
        }

        [Fact]
        public void History_masks_credentials_inside_a_route_override()
        {
            var route = new ProxyRouteConfig
            {
                Method = HttpMethodType.Get,
                Path = "orders",
                Headers = new List<ProxyKeyValue> { new() { Key = "Authorization", Value = "Bearer plain_secret" } },
            };
            var field = "route:" + ProxyRouteCodec.AddressOf(route);

            var shown = ProxyChangeSet.MaskForDisplay(field, ProxyRouteCodec.Encode(route));

            shown.Should().NotContain("plain_secret").And.Contain("***");
            ProxyChangeSet.MaskForDisplay(field, "not json").Should().Be("***", "an unreadable value is never shown raw");
        }

        [Fact]
        public void Upstream_with_user_info_is_refused_and_never_shown_masked()
        {
            var result = ProxyConfigValidator.Validate("X", "https://user:pass@api.x.com/v1", new[] { "GET" }, null, null);

            result.Errors.Should().ContainKey("upstream");
            ProxyUpstreamMasker.Mask("https://user:pass@api.example.com/v1").Should().NotContain("pass").And.NotContain("user");
        }
    }
}
