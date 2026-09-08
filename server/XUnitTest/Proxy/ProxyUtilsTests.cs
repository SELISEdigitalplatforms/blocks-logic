using System.Text;
using FluentAssertions;
using Proxy.DomainService.Dtos;
using Proxy.DomainService.Entities;
using Proxy.DomainService.Utils;

namespace XUnitTest.Proxy
{
    public class ProxyUtilsTests
    {
        // ---------- ProxySlug ----------
        [Theory]
        [InlineData("Stripe Payments", "stripe-payments")]
        [InlineData("Stripe  Payments!", "stripe-payments")]
        [InlineData("  Weather Lookup  ", "weather-lookup")]
        [InlineData("A/B/C", "a-b-c")]
        [InlineData("--Edge--", "edge")]
        [InlineData("こんにちは", "")]
        public void ProxySlug_From_DerivesExpectedSlug(string name, string expected)
        {
            ProxySlug.From(name).Should().Be(expected);
        }

        // ---------- ProxyUpstreamMasker ----------
        [Theory]
        [InlineData("https://api.stripe.com/v1/charges", "api.st••••.com/•••")]
        [InlineData("https://api.weatherapi.com/v1/current.json", "api.we••••.com/•••")]
        [InlineData("https://example.com/x", "example.com/•••")]
        [InlineData("https://localhost", "localhost/•••")]
        public void ProxyUpstreamMasker_Mask_ObscuresMiddleLabels(string upstream, string expected)
        {
            ProxyUpstreamMasker.Mask(upstream).Should().Be(expected);
        }

        // ---------- ProxyConfigLine ----------
        [Fact]
        public void ProxyConfigLine_From_FormatsOneLine()
        {
            var snapshot = new ProxyConfigSnapshot
            {
                Upstream = "https://api.stripe.com/v1/charges",
                Methods = new() { HttpMethodType.Get, HttpMethodType.Post },
                Enabled = true,
                Headers = new() { new ProxyKeyValue { Key = "Authorization", Value = "x" } },
                Query = new(),
            };

            ProxyConfigLine.From(snapshot)
                .Should().Be("api.stripe.com/v1/charges · [GET, POST] · enabled · 1 header(s) · 0 query param(s)");
        }

        [Fact]
        public void ProxyConfigLine_From_UsesDisabledWord()
        {
            var snapshot = new ProxyConfigSnapshot { Upstream = "https://a.example.com", Methods = new() { HttpMethodType.Get }, Enabled = false };
            ProxyConfigLine.From(snapshot).Should().Be("a.example.com · [GET] · disabled · 0 header(s) · 0 query param(s)");
        }

        // ---------- ProxySecretRef ----------
        [Theory]
        [InlineData("Bearer ${SECRET.STRIPE_KEY}", true)]
        [InlineData("${SECRET.A_B_9}", true)]
        [InlineData("plain-value", false)]
        [InlineData("${SECRET.}", false)]
        [InlineData("", false)]
        public void ProxySecretRef_IsSecretReference_MatchesTokenSyntax(string value, bool expected)
        {
            ProxySecretRef.IsSecretReference(value).Should().Be(expected);
        }

        [Fact]
        public void ProxySecretRef_Tokens_YieldsEveryOccurrenceInOrder()
        {
            ProxySecretRef.Tokens("${SECRET.A} and ${SECRET.B} and ${SECRET.A}")
                .Should().Equal("${SECRET.A}", "${SECRET.B}", "${SECRET.A}");
            ProxySecretRef.Tokens("plain").Should().BeEmpty();
            ProxySecretRef.Tokens(null).Should().BeEmpty();
        }

        // ---------- ProxyStatusClassParser ----------
        [Theory]
        [InlineData(null, ProxyStatusClass.All)]
        [InlineData("", ProxyStatusClass.All)]
        [InlineData("all", ProxyStatusClass.All)]
        [InlineData("2xx", ProxyStatusClass.TwoXx)]
        [InlineData("4XX", ProxyStatusClass.FourXx)]
        [InlineData(" 5xx ", ProxyStatusClass.FiveXx)]
        public void ProxyStatusClassParser_TryParse_AcceptsKnownValues(string? input, ProxyStatusClass expected)
        {
            ProxyStatusClassParser.TryParse(input, out var result).Should().BeTrue();
            result.Should().Be(expected);
        }

        [Theory]
        [InlineData("3xx")]
        [InlineData("200")]
        [InlineData("error")]
        public void ProxyStatusClassParser_TryParse_RejectsUnknownValues(string input)
        {
            ProxyStatusClassParser.TryParse(input, out _).Should().BeFalse();
            ProxyStatusClassParser.InvalidMessage.Should().Be("Must be one of all, 2xx, 4xx, 5xx.");
        }

        // ---------- ProxyCsvWriter ----------
        [Fact]
        public void ProxyCsvWriter_Write_EmitsBomHeaderAndCrlf()
        {
            var bytes = ProxyCsvWriter.Write(Array.Empty<ProxyExecutionEntity>());

            bytes.Take(3).Should().Equal(0xEF, 0xBB, 0xBF); // UTF-8 BOM
            Encoding.UTF8.GetString(bytes).TrimStart('﻿')
                .Should().Be("Time,Method,Path,Status,LatencyMs,UpstreamHost,Outcome,Error\r\n");
        }

        [Fact]
        public void ProxyCsvWriter_Write_QuotesFieldsWithSeparatorsAndDoublesQuotes()
        {
            var row = new ProxyExecutionEntity
            {
                TenantId = "t",
                ProxyId = "p",
                ProxySlug = "s",
                RequestMethod = "GET",
                RequestPath = "/a,b",
                RequestQuery = string.Empty,
                UpstreamUrl = "https://x",
                UpstreamHost = "x",
                Outcome = "Success",
                StatusCode = 200,
                LatencyMs = 5,
                StartedAtUtc = new DateTime(2026, 9, 7, 10, 15, 0, DateTimeKind.Utc),
                ErrorMessage = "he said \"hi\"",
            };

            var line = Encoding.UTF8.GetString(ProxyCsvWriter.Write(new[] { row }))
                .TrimStart('﻿')
                .Split("\r\n", StringSplitOptions.RemoveEmptyEntries)[1];

            line.Should().Be("2026-09-07T10:15:00.000Z,GET,\"/a,b\",200,5,x,Success,\"he said \"\"hi\"\"\"");
        }

        // ---------- ProxyConfigValidator ----------
        [Fact]
        public void Validator_NormalizesMethodsAndFlagsSecretRefs()
        {
            var result = ProxyConfigValidator.Validate(
                "  Stripe Payments  ",
                " https://api.stripe.com/v1/charges ",
                new[] { "get", "POST", "get" },
                new[] { new ProxyKeyValueInputDto { Key = "Authorization", Value = "Bearer ${SECRET.K}" } },
                null);

            result.IsValid.Should().BeTrue();
            result.Name.Should().Be("Stripe Payments");
            result.Upstream.Should().Be("https://api.stripe.com/v1/charges");
            result.Methods.Should().Equal(HttpMethodType.Get, HttpMethodType.Post);
            result.Headers[0].IsSecretRef.Should().BeTrue();
        }

        [Fact]
        public void Validator_HonoursExplicitVaultFlagOnPlainValue()
        {
            var result = ProxyConfigValidator.Validate(
                "Name",
                "https://api.stripe.com",
                new[] { "GET" },
                new[] { new ProxyKeyValueInputDto { Key = "X-Api-Key", Value = "abc123", IsSecretRef = true } },
                new[] { new ProxyKeyValueInputDto { Key = "token", Value = "plain", IsSecretRef = false } });

            result.IsValid.Should().BeTrue();
            result.Headers[0].IsSecretRef.Should().BeTrue();
            result.Query[0].IsSecretRef.Should().BeFalse();
        }

        [Theory]
        [InlineData("ftp://x")]
        [InlineData("http://api.stripe.com")]
        [InlineData("not a url")]
        [InlineData("")]
        public void Validator_RejectsNonHttpsUpstream(string upstream)
        {
            var result = ProxyConfigValidator.Validate("Name", upstream, new[] { "GET" }, null, null);

            result.IsValid.Should().BeFalse();
            result.Errors.Should().ContainKey("upstream").WhoseValue.Should().Be("Must be an absolute https:// URL.");
        }

        [Fact]
        public void Validator_RejectsEmptyName()
        {
            var result = ProxyConfigValidator.Validate("   ", "https://a.com", new[] { "GET" }, null, null);
            result.Errors.Should().ContainKey("name");
        }

        [Fact]
        public void Validator_RejectsNameOver80Chars()
        {
            var result = ProxyConfigValidator.Validate(new string('a', 81), "https://a.com", new[] { "GET" }, null, null);
            result.Errors.Should().ContainKey("name");
        }

        [Fact]
        public void Validator_RejectsMethodOutsideAllowedSet()
        {
            var result = ProxyConfigValidator.Validate(
                "X", "https://api.x.com", new[] { "GET", "POST", "PUT", "PATCH", "DELETE", "HEAD" }, null, null);

            result.Errors.Should().ContainKey("methods")
                .WhoseValue.Should().Be("Only GET, POST, PUT, PATCH, DELETE are allowed.");
        }

        [Fact]
        public void Validator_RejectsEmptyMethods()
        {
            var result = ProxyConfigValidator.Validate("X", "https://api.x.com", Array.Empty<string>(), null, null);
            result.Errors.Should().ContainKey("methods");
        }

        [Fact]
        public void Validator_RejectsBlankHeaderKey()
        {
            var result = ProxyConfigValidator.Validate(
                "X", "https://api.x.com", new[] { "GET" },
                new[] { new ProxyKeyValueInputDto { Key = "  ", Value = "v" } }, null);

            result.Errors.Should().ContainKey("headers");
        }

        [Fact]
        public void Validator_RejectsOversizedQueryValue()
        {
            var result = ProxyConfigValidator.Validate(
                "X", "https://api.x.com", new[] { "GET" }, null,
                new[] { new ProxyKeyValueInputDto { Key = "q", Value = new string('v', 4097) } });

            result.Errors.Should().ContainKey("query");
        }

        [Fact]
        public void Validator_NormalizesMethodConfig_CollapsesEmptyMembersAndFlagsSecretRefs()
        {
            var result = ProxyConfigValidator.Validate(
                "X", "https://api.x.com", new[] { "GET", "POST" }, null, null,
                new[]
                {
                    new ProxyMethodConfigInputDto
                    {
                        Method = "post",
                        Upstream = "  https://api.x.com/v2  ",
                        Headers = new List<ProxyKeyValueInputDto>
                        {
                            new() { Key = "Authorization", Value = "Bearer ${SECRET.K}" },
                        },
                        Query = new List<ProxyKeyValueInputDto>(),
                    },
                });

            result.IsValid.Should().BeTrue();
            result.MethodConfigs.Should().ContainSingle();
            var config = result.MethodConfigs[0];
            config.Method.Should().Be(HttpMethodType.Post);
            config.Upstream.Should().Be("https://api.x.com/v2");
            config.Headers.Should().ContainSingle().Which.IsSecretRef.Should().BeTrue();
            config.Query.Should().BeNull();
        }

        [Fact]
        public void Validator_DropsAllInheritMethodConfigEntry()
        {
            var result = ProxyConfigValidator.Validate(
                "X", "https://api.x.com", new[] { "GET" }, null, null,
                new[] { new ProxyMethodConfigInputDto { Method = "GET" } });

            result.IsValid.Should().BeTrue();
            result.MethodConfigs.Should().BeEmpty();
        }

        [Fact]
        public void Validator_RejectsMethodConfigForNonAllowedMethod()
        {
            var result = ProxyConfigValidator.Validate(
                "X", "https://api.x.com", new[] { "GET" }, null, null,
                new[] { new ProxyMethodConfigInputDto { Method = "POST", Upstream = "https://api.x.com/v2" } });

            result.Errors.Should().ContainKey("methodConfigs")
                .WhoseValue.Should().Be("Per-method overrides must target an allowed method.");
        }

        [Fact]
        public void Validator_RejectsDuplicateMethodConfig()
        {
            var result = ProxyConfigValidator.Validate(
                "X", "https://api.x.com", new[] { "GET" }, null, null,
                new[]
                {
                    new ProxyMethodConfigInputDto { Method = "GET", Upstream = "https://api.x.com/a" },
                    new ProxyMethodConfigInputDto { Method = "GET", Upstream = "https://api.x.com/b" },
                });

            result.Errors.Should().ContainKey("methodConfigs")
                .WhoseValue.Should().Be("Only one override per method is allowed.");
        }

        [Fact]
        public void Validator_RejectsNonHttpsMethodConfigUpstream()
        {
            var result = ProxyConfigValidator.Validate(
                "X", "https://api.x.com", new[] { "GET" }, null, null,
                new[] { new ProxyMethodConfigInputDto { Method = "GET", Upstream = "http://api.x.com/v2" } });

            result.Errors.Should().ContainKey("methodConfigs")
                .WhoseValue.Should().Be("Each per-method upstream must be an absolute https:// URL.");
        }

        [Fact]
        public void Validator_AllowsNullOrEmptyMethodConfigs()
        {
            ProxyConfigValidator.Validate("X", "https://api.x.com", new[] { "GET" }, null, null, null)
                .Errors.Should().NotContainKey("methodConfigs");
            ProxyConfigValidator.Validate(
                    "X", "https://api.x.com", new[] { "GET" }, null, null, Array.Empty<ProxyMethodConfigInputDto>())
                .Errors.Should().NotContainKey("methodConfigs");
        }

        // ---------- ProxyConfigValidator: SSRF guard (PR 3) ----------

        [Theory]
        [InlineData("https://169.254.169.254/latest/meta-data/")]
        [InlineData("https://127.0.0.1/admin")]
        [InlineData("https://10.1.2.3/internal")]
        [InlineData("https://192.168.0.10/")]
        [InlineData("https://172.16.5.5/")]
        [InlineData("https://[::1]/")]
        public void Validator_RejectsPrivateOrLoopbackLiteralUpstream(string upstream)
        {
            var result = ProxyConfigValidator.Validate("X", upstream, new[] { "GET" }, null, null);

            result.IsValid.Should().BeFalse();
            result.Errors.Should().ContainKey("upstream")
                .WhoseValue.Should().Contain("private, loopback, or link-local");
        }

        [Fact]
        public void Validator_RejectsPrivateLiteralPerMethodUpstream()
        {
            var result = ProxyConfigValidator.Validate(
                "X", "https://api.x.com", new[] { "GET" }, null, null,
                new[] { new ProxyMethodConfigInputDto { Method = "GET", Upstream = "https://169.254.169.254/" } });

            result.Errors.Should().ContainKey("methodConfigs")
                .WhoseValue.Should().Contain("private, loopback, or link-local");
        }

        [Fact]
        public void Validator_AllowsPublicHostnameUpstream()
        {
            var result = ProxyConfigValidator.Validate(
                "X", "https://api.stripe.com/v1/charges", new[] { "GET" }, null, null);

            result.Errors.Should().NotContainKey("upstream");
        }

        // ---------- ProxyConfigValidator: duplicate header keys (PR 4) ----------

        [Fact]
        public void Validator_RejectsDuplicateHeaderKeys_CaseInsensitive()
        {
            var result = ProxyConfigValidator.Validate(
                "X", "https://api.x.com", new[] { "GET" },
                new[]
                {
                    new ProxyKeyValueInputDto { Key = "X-Api-Key", Value = "a" },
                    new ProxyKeyValueInputDto { Key = "x-api-key", Value = "b" },
                },
                null);

            result.Errors.Should().ContainKey("headers")
                .WhoseValue.Should().Be("Each header key must be unique (case-insensitive).");
        }

        [Fact]
        public void Validator_AllowsDuplicateQueryKeys()
        {
            var result = ProxyConfigValidator.Validate(
                "X", "https://api.x.com", new[] { "GET" }, null,
                new[]
                {
                    new ProxyKeyValueInputDto { Key = "tag", Value = "a" },
                    new ProxyKeyValueInputDto { Key = "tag", Value = "b" },
                });

            result.Errors.Should().NotContainKey("query");
        }
    }
}
