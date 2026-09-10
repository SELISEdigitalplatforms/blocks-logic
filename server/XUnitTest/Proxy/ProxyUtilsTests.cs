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

        // ---------- ProxyVarRef ----------
        [Theory]
        [InlineData("Bearer {{$VAR.stripe-key}}", true)]
        [InlineData("{{$VAR.a.b_9:x-y}}", true)]
        [InlineData("plain-value", false)]
        [InlineData("{{$VAR.}}", false)]           // empty name
        [InlineData("{{ $VAR.x }}", false)]        // whitespace inside braces
        [InlineData("{{$var.x}}", false)]          // wrong case
        [InlineData("{{VAR.x}}", false)]           // missing $
        [InlineData("${SECRET.X}", false)]         // the old syntax is now literal text
        [InlineData("", false)]
        public void ProxyVarRef_ContainsRef_MatchesTokenSyntax(string value, bool expected)
        {
            ProxyVarRef.ContainsRef(value).Should().Be(expected);
        }

        [Fact]
        public void ProxyVarRef_Tokens_YieldsEveryOccurrenceInOrder()
        {
            ProxyVarRef.Tokens("{{$VAR.a}} and {{$VAR.b}} and {{$VAR.a}}")
                .Should().Equal("{{$VAR.a}}", "{{$VAR.b}}", "{{$VAR.a}}");
            ProxyVarRef.Tokens("plain").Should().BeEmpty();
            ProxyVarRef.Tokens(null).Should().BeEmpty();
        }

        [Fact]
        public void ProxyVarRef_Names_DeduplicatesAcrossListsInFirstSeenOrder()
        {
            var headers = new List<ProxyKeyValue>
            {
                new() { Key = "Authorization", Value = "Bearer {{$VAR.token}}" },
                new() { Key = "X-Extra", Value = "{{$VAR.token}} {{$VAR.other}}" },
            };
            var query = new List<ProxyKeyValue> { new() { Key = "k", Value = "{{$VAR.token}}" } };

            ProxyVarRef.Names(headers, query, null).Should().Equal("token", "other");
        }

        [Fact]
        public void ProxyVarRef_Substitute_ReplacesEveryToken_ThrowsOnMissingName()
        {
            var map = new Dictionary<string, string>(StringComparer.Ordinal) { ["token"] = "abc", ["k"] = "v" };

            ProxyVarRef.Substitute("Bearer {{$VAR.token}}", map).Should().Be("Bearer abc");
            ProxyVarRef.Substitute("{{$VAR.token}}-{{$VAR.k}}-{{$VAR.token}}", map).Should().Be("abc-v-abc");
            ProxyVarRef.Substitute("no tokens here", map).Should().Be("no tokens here");

            var act = () => ProxyVarRef.Substitute("{{$VAR.unknown}}", map);
            act.Should().Throw<KeyNotFoundException>();
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
        public void Validator_NormalizesMethodsAndStoresValueVerbatim()
        {
            var result = ProxyConfigValidator.Validate(
                "  Stripe Payments  ",
                " https://api.stripe.com/v1/charges ",
                new[] { "get", "POST", "get" },
                new[] { new ProxyKeyValueInputDto { Key = "Authorization", Value = "Bearer {{$VAR.k}}" } },
                null);

            result.IsValid.Should().BeTrue();
            result.Name.Should().Be("Stripe Payments");
            result.Upstream.Should().Be("https://api.stripe.com/v1/charges");
            result.Methods.Should().Equal(HttpMethodType.Get, HttpMethodType.Post);
            result.Headers[0].Value.Should().Be("Bearer {{$VAR.k}}");
        }

        [Fact]
        public void Validator_NormalizesBodyMerge_StoresTokenVerbatim_AndDropsBlankRows()
        {
            var result = ProxyConfigValidator.Validate(
                "Name", "https://api.x.com", new[] { "POST" }, null, null, null,
                new[]
                {
                    new ProxyKeyValueInputDto { Key = "account", Value = "acct_123" },
                    new ProxyKeyValueInputDto { Key = "api_key", Value = "{{$VAR.k}}" },
                });

            result.IsValid.Should().BeTrue();
            result.BodyMerge.Should().HaveCount(2);
            result.BodyMerge[0].Value.Should().Be("acct_123");
            result.BodyMerge[1].Value.Should().Be("{{$VAR.k}}");
        }

        [Fact]
        public void Validator_RejectsDuplicateBodyMergeKey_OrdinalCaseSensitive()
        {
            var result = ProxyConfigValidator.Validate(
                "Name", "https://api.x.com", new[] { "POST" }, null, null, null,
                new[]
                {
                    new ProxyKeyValueInputDto { Key = "account", Value = "a" },
                    new ProxyKeyValueInputDto { Key = "account", Value = "b" },
                });

            result.IsValid.Should().BeFalse();
            result.Errors.Should().ContainKey("bodyMerge")
                .WhoseValue.Should().Be("Each body field key must be unique.");
        }

        [Fact]
        public void Validator_AllowsBodyMergeKeysDifferingOnlyInCase()
        {
            var result = ProxyConfigValidator.Validate(
                "Name", "https://api.x.com", new[] { "POST" }, null, null, null,
                new[]
                {
                    new ProxyKeyValueInputDto { Key = "Account", Value = "a" },
                    new ProxyKeyValueInputDto { Key = "account", Value = "b" },
                });

            result.IsValid.Should().BeTrue();
            result.BodyMerge.Should().HaveCount(2);
        }

        [Fact]
        public void Validator_RejectsOversizedBodyMergeValue()
        {
            var result = ProxyConfigValidator.Validate(
                "Name", "https://api.x.com", new[] { "POST" }, null, null, null,
                new[] { new ProxyKeyValueInputDto { Key = "big", Value = new string('x', 4097) } });

            result.IsValid.Should().BeFalse();
            result.Errors.Should().ContainKey("bodyMerge");
        }

        // ---------- ProxyConfigValidator: response field filtering ----------

        [Theory]
        [InlineData("select", ProxyResponseMode.Select)]
        [InlineData("SELECT", ProxyResponseMode.Select)]
        [InlineData("All", ProxyResponseMode.All)]
        [InlineData("", ProxyResponseMode.All)]
        [InlineData(null, ProxyResponseMode.All)]
        public void Validator_ParsesResponseMode_CaseInsensitive(string? mode, ProxyResponseMode expected)
        {
            var result = ProxyConfigValidator.Validate(
                "X", "https://api.x.com", new[] { "GET" }, null, null, null, null, mode, null);

            result.IsValid.Should().BeTrue();
            result.ResponseMode.Should().Be(expected);
        }

        [Fact]
        public void Validator_RejectsUnknownResponseMode()
        {
            var result = ProxyConfigValidator.Validate(
                "X", "https://api.x.com", new[] { "GET" }, null, null, null, null, "trim", null);

            result.IsValid.Should().BeFalse();
            result.Errors.Should().ContainKey("responseMode")
                .WhoseValue.Should().Be("responseMode must be 'All' or 'Select'.");
        }

        [Fact]
        public void Validator_NormalizesResponseInclude_TrimsDedupesDropsBlank()
        {
            var result = ProxyConfigValidator.Validate(
                "X", "https://api.x.com", new[] { "GET" }, null, null, null, null, "Select",
                new[] { " data.id ", "data.id", "", "   ", "data.name" });

            result.IsValid.Should().BeTrue();
            result.ResponseInclude.Should().Equal("data.id", "data.name");
        }

        [Fact]
        public void Validator_RejectsInvalidResponsePath()
        {
            var result = ProxyConfigValidator.Validate(
                "X", "https://api.x.com", new[] { "GET" }, null, null, null, null, "Select",
                new[] { "items[0]" });

            result.IsValid.Should().BeFalse();
            result.Errors.Should().ContainKey("responseInclude")
                .WhoseValue.Should().Be("'items[0]' is not a valid field path.");
        }

        [Fact]
        public void Validator_RejectsOverTwoHundredResponsePaths()
        {
            var paths = Enumerable.Range(0, 201).Select(i => $"f{i}").ToArray();
            var result = ProxyConfigValidator.Validate(
                "X", "https://api.x.com", new[] { "GET" }, null, null, null, null, "Select", paths);

            result.IsValid.Should().BeFalse();
            result.Errors.Should().ContainKey("responseInclude")
                .WhoseValue.Should().Be("At most 200 response fields.");
        }

        [Fact]
        public void Validator_EmptyResponseIncludeUnderSelect_IsValid()
        {
            var result = ProxyConfigValidator.Validate(
                "X", "https://api.x.com", new[] { "GET" }, null, null, null, null, "Select", Array.Empty<string>());

            result.IsValid.Should().BeTrue();
            result.ResponseMode.Should().Be(ProxyResponseMode.Select);
            result.ResponseInclude.Should().BeEmpty();
        }

        [Fact]
        public void Validator_KeepsResponsePaths_EvenWhenModeIsAll()
        {
            var result = ProxyConfigValidator.Validate(
                "X", "https://api.x.com", new[] { "GET" }, null, null, null, null, "All",
                new[] { "data.id" });

            result.IsValid.Should().BeTrue();
            result.ResponseMode.Should().Be(ProxyResponseMode.All);
            result.ResponseInclude.Should().Equal("data.id");
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
        public void Validator_NormalizesMethodConfig_CollapsesEmptyMembers_StoresValueVerbatim()
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
                            new() { Key = "Authorization", Value = "Bearer {{$VAR.k}}" },
                        },
                        Query = new List<ProxyKeyValueInputDto>(),
                    },
                });

            result.IsValid.Should().BeTrue();
            result.MethodConfigs.Should().ContainSingle();
            var config = result.MethodConfigs[0];
            config.Method.Should().Be(HttpMethodType.Post);
            config.Upstream.Should().Be("https://api.x.com/v2");
            config.Headers.Should().ContainSingle().Which.Value.Should().Be("Bearer {{$VAR.k}}");
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
