using System.Text;
using System.Text.Json;
using FluentAssertions;
using Proxy.DomainService.Entities;
using Proxy.DomainService.Utils;

namespace XUnitTest.Proxy
{
    /// <summary>
    /// Covers <see cref="ProxyBodyMerger.Merge"/>: an absent / empty body starts from <c>{}</c>, an existing
    /// object gains the fields (a colliding key is overridden, unrelated keys are preserved), a non-object body
    /// is flagged <c>NotMergeable</c>, <c>{{$VAR.name}}</c> tokens are substituted from the resolved map, and
    /// the output is minified UTF-8 JSON.
    /// </summary>
    public class ProxyBodyMergerTests
    {
        private static readonly IReadOnlyDictionary<string, string> NoVars =
            new Dictionary<string, string>(StringComparer.Ordinal);

        private static IReadOnlyDictionary<string, string> Vars(params (string Name, string Value)[] pairs) =>
            pairs.ToDictionary(p => p.Name, p => p.Value, StringComparer.Ordinal);

        private static List<ProxyKeyValue> Fields(params (string Key, string Value)[] pairs) =>
            pairs.Select(p => new ProxyKeyValue { Key = p.Key, Value = p.Value }).ToList();

        private static JsonElement Parse(byte[] body) => JsonDocument.Parse(body).RootElement;

        [Fact]
        public void Merge_NullBody_StartsFromEmptyObject()
        {
            var result = ProxyBodyMerger.Merge(null, Fields(("account", "acct_123")), NoVars);

            result.NotMergeable.Should().BeFalse();
            var root = Parse(result.Body!);
            root.GetProperty("account").GetString().Should().Be("acct_123");
            root.EnumerateObject().Should().HaveCount(1);
        }

        [Fact]
        public void Merge_EmptyBody_StartsFromEmptyObject()
        {
            var result = ProxyBodyMerger.Merge(Array.Empty<byte>(), Fields(("a", "1")), NoVars);

            result.NotMergeable.Should().BeFalse();
            Parse(result.Body!).GetProperty("a").GetString().Should().Be("1");
        }

        [Fact]
        public void Merge_ExistingObject_AddsFields_OverridesCollidingKey_KeepsOthers()
        {
            var body = Encoding.UTF8.GetBytes("{\"amount\":10,\"account\":\"client_val\",\"nested\":{\"x\":1}}");

            var result = ProxyBodyMerger.Merge(body, Fields(("account", "acct_123"), ("api_key", "k")), NoVars);

            result.NotMergeable.Should().BeFalse();
            var root = Parse(result.Body!);
            root.GetProperty("amount").GetInt32().Should().Be(10);
            root.GetProperty("account").GetString().Should().Be("acct_123"); // configured wins
            root.GetProperty("api_key").GetString().Should().Be("k");
            root.GetProperty("nested").GetProperty("x").GetInt32().Should().Be(1); // untouched
        }

        [Fact]
        public void Merge_VariableToken_IsSubstitutedFromResolvedMap()
        {
            var result = ProxyBodyMerger.Merge(
                Encoding.UTF8.GetBytes("{}"),
                Fields(("api_key", "{{$VAR.demo-key}}")),
                Vars(("demo-key", "sk_live_42")));

            Parse(result.Body!).GetProperty("api_key").GetString().Should().Be("sk_live_42");
        }

        [Fact]
        public void Merge_EmbeddedVariableToken_IsSubstitutedInPlace()
        {
            var result = ProxyBodyMerger.Merge(
                Encoding.UTF8.GetBytes("{}"),
                Fields(("authorization", "Bearer {{$VAR.token}}")),
                Vars(("token", "abc123")));

            Parse(result.Body!).GetProperty("authorization").GetString().Should().Be("Bearer abc123");
        }

        [Fact]
        public void Merge_NonTokenField_IsWrittenUnchanged()
        {
            var result = ProxyBodyMerger.Merge(
                Encoding.UTF8.GetBytes("{}"), Fields(("plain", "literal")), Vars(("token", "abc123")));

            Parse(result.Body!).GetProperty("plain").GetString().Should().Be("literal");
        }

        [Fact]
        public void Merge_ValuesAreAlwaysWrittenAsJsonStrings()
        {
            var result = ProxyBodyMerger.Merge(
                Encoding.UTF8.GetBytes("{}"), Fields(("count", "10"), ("flag", "true")), NoVars);

            var root = Parse(result.Body!);
            root.GetProperty("count").ValueKind.Should().Be(JsonValueKind.String);
            root.GetProperty("flag").ValueKind.Should().Be(JsonValueKind.String);
        }

        [Fact]
        public void Merge_OutputIsMinifiedUtf8()
        {
            var result = ProxyBodyMerger.Merge(
                Encoding.UTF8.GetBytes("{ \"amount\" : 10 }"), Fields(("account", "acct_123")), NoVars);

            Encoding.UTF8.GetString(result.Body!).Should().Be("{\"amount\":10,\"account\":\"acct_123\"}");
        }

        [Theory]
        [InlineData("[1,2,3]")]
        [InlineData("\"just a string\"")]
        [InlineData("42")]
        [InlineData("true")]
        [InlineData("null")]
        [InlineData("{ not json")]
        [InlineData("{\"a\":1")]
        public void Merge_NonObjectOrMalformedBody_IsNotMergeable(string raw)
        {
            var result = ProxyBodyMerger.Merge(Encoding.UTF8.GetBytes(raw), Fields(("a", "1")), NoVars);

            result.NotMergeable.Should().BeTrue();
            result.Body.Should().BeNull();
        }
    }
}
