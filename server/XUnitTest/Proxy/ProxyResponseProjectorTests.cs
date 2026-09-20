using System.Text;
using System.Text.Json;
using FluentAssertions;
using Proxy.DomainService.Entities;
using Proxy.DomainService.Utils;

namespace XUnitTest.Proxy
{
    /// <summary>
    /// Covers <see cref="ProxyResponseProjector.Project"/> against SPEC &sect;2.2 / &sect;2.3 / &sect;8: the
    /// step order, the whole-primitive carve-out, the keep rules (M1&ndash;M5) and their edge cases, the
    /// empty-result cases, and the fail-closed cases (each returning a <c>null</c> body and a reason that
    /// contains no upstream body text).
    /// </summary>
    public class ProxyResponseProjectorTests
    {
        private const string Json = "application/json";

        private static (byte[]? Body, string? ContentType, ProxyResponseFilterNote Note, string? FailReason) Project(
            string body, string? contentType = Json, int status = 200, params string[] include) =>
            ProxyResponseProjector.Project(
                Encoding.UTF8.GetBytes(body), contentType, status, ProxyResponseMode.Select, include);

        private static string Canonical(byte[] bytes) =>
            JsonSerializer.Serialize(JsonSerializer.Deserialize<JsonElement>(bytes));

        // ---- step order ---------------------------------------------------------

        [Fact]
        public void AllMode_ShortCircuitsBeforeAnyCheck()
        {
            var body = Encoding.UTF8.GetBytes("not json at all");
            var (relay, ct, note, reason) = ProxyResponseProjector.Project(
                body, "text/plain", 500, ProxyResponseMode.All, new[] { "a" });

            relay.Should().BeSameAs(body);
            ct.Should().Be("text/plain");
            note.Should().Be(ProxyResponseFilterNote.NotConfigured);
            reason.Should().BeNull();
        }

        [Fact]
        public void NonSuccessStatus_FailsBeforeContentTypeCheck()
        {
            var (relay, _, note, reason) = Project("<html/>", "text/html", 503, "a");

            relay.Should().BeNull();
            note.Should().Be(ProxyResponseFilterNote.Failed);
            reason.Should().Be("upstream returned 503");
        }

        [Fact]
        public void OversizedBody_FailsBeforeParse()
        {
            var big = new string('x', (int)ProxyResponseProjector.MaxProjectableBytes + 1);
            var body = Encoding.UTF8.GetBytes("\"" + big + "\"");

            var (relay, _, note, reason) = ProxyResponseProjector.Project(
                body, Json, 200, ProxyResponseMode.Select, new[] { "a" });

            relay.Should().BeNull();
            note.Should().Be(ProxyResponseFilterNote.Failed);
            reason.Should().Contain("over the 5 MB filtering limit");
        }

        // ---- whole-primitive --------------------------------------------------------

        [Theory]
        [InlineData("\"hello\"")]
        [InlineData("42")]
        [InlineData("true")]
        [InlineData("false")]
        [InlineData("null")]
        [InlineData("[]")]
        [InlineData("[1,2,3]")]
        [InlineData("[\"a\",\"b\"]")]
        public void WholePrimitive_RelaysUnchanged(string body)
        {
            var raw = Encoding.UTF8.GetBytes(body);
            var (relay, ct, note, reason) = ProxyResponseProjector.Project(
                raw, Json, 200, ProxyResponseMode.Select, new[] { "a", "b" });

            relay.Should().BeSameAs(raw);
            ct.Should().Be(Json);
            note.Should().Be(ProxyResponseFilterNote.WholePrimitive);
            reason.Should().BeNull();
        }

        // ---- Applied --------------------------------------------------------

        [Fact]
        public void Applied_NestedObjectSubset()
        {
            var (relay, ct, note, _) = Project(
                """{"data":{"id":1,"secret":"x"},"meta":{"page":2}}""", Json, 200, "data.id");

            Canonical(relay!).Should().Be("""{"data":{"id":1}}""");
            ct.Should().Be("application/json; charset=utf-8");
            note.Should().Be(ProxyResponseFilterNote.Applied);
        }

        [Fact]
        public void Applied_ArrayEach_MissingKeyKeepsSlot_LengthPreserved()
        {
            var (relay, _, note, _) = Project(
                """{"items":[{"id":1,"x":9},{"x":9},{"id":3}]}""", Json, 200, "items[].id");

            Canonical(relay!).Should().Be("""{"items":[{"id":1},{},{"id":3}]}""");
            note.Should().Be(ProxyResponseFilterNote.Applied);
        }

        [Fact]
        public void Applied_ArrayMarkerIgnored_DottedPathMatchesArray()
        {
            var dotted = Project("""{"items":[{"id":1},{"id":2}]}""", Json, 200, "items.id");
            var marked = Project("""{"items":[{"id":1},{"id":2}]}""", Json, 200, "items[].id");

            Canonical(dotted.Body!).Should().Be("""{"items":[{"id":1},{"id":2}]}""");
            Canonical(marked.Body!).Should().Be(Canonical(dotted.Body!));
        }

        [Fact]
        public void Applied_TerminalParentMatch_KeepsWholeSubtree()
        {
            var (relay, _, _, _) = Project(
                """{"data":{"user":{"email":"e","name":"n"},"other":1}}""", Json, 200,
                "data", "data.user.email");

            Canonical(relay!).Should().Be("""{"data":{"user":{"email":"e","name":"n"},"other":1}}""");
        }

        [Fact]
        public void Applied_IntermediateEmptyContainerKept()
        {
            var (relay, _, note, _) = Project(
                """{"a":{"b":{"z":1}}}""", Json, 200, "a.b.c");

            Canonical(relay!).Should().Be("""{"a":{"b":{}}}""");
            note.Should().Be(ProxyResponseFilterNote.Applied);
        }

        [Fact]
        public void Applied_NullLeafKept()
        {
            var (relay, _, _, _) = Project("""{"a":null,"b":1}""", Json, 200, "a");

            Canonical(relay!).Should().Be("""{"a":null}""");
        }

        [Fact]
        public void Applied_PrimitiveArrayElementUnderObjectPathEmittedAsIs()
        {
            var (relay, _, _, _) = Project("""{"items":["a","b"]}""", Json, 200, "items[].id");

            Canonical(relay!).Should().Be("""{"items":["a","b"]}""");
        }

        [Fact]
        public void Applied_DeadEndPrimitiveParentOmitted()
        {
            var (relay, _, note, _) = Project("""{"a":5,"b":{"c":1}}""", Json, 200, "a.b", "b.c");

            Canonical(relay!).Should().Be("""{"b":{"c":1}}""");
            note.Should().Be(ProxyResponseFilterNote.Applied);
        }

        // ---- EmptyResult --------------------------------------------------------

        [Fact]
        public void EmptyResult_EmptyIncludeList_ObjectRoot()
        {
            var (relay, ct, note, _) = ProxyResponseProjector.Project(
                Encoding.UTF8.GetBytes("""{"a":1}"""), Json, 200, ProxyResponseMode.Select, Array.Empty<string>());

            Encoding.UTF8.GetString(relay!).Should().Be("{}");
            ct.Should().Be("application/json; charset=utf-8");
            note.Should().Be(ProxyResponseFilterNote.EmptyResult);
        }

        [Fact]
        public void EmptyResult_NothingMatched()
        {
            var (relay, _, note, _) = Project("""{"a":1}""", Json, 200, "nope.not.here");

            Encoding.UTF8.GetString(relay!).Should().Be("{}");
            note.Should().Be(ProxyResponseFilterNote.EmptyResult);
        }

        [Fact]
        public void EmptyResult_EmptyBody()
        {
            var (relay, ct, note, _) = ProxyResponseProjector.Project(
                Array.Empty<byte>(), Json, 200, ProxyResponseMode.Select, new[] { "a" });

            relay.Should().BeEmpty();
            ct.Should().Be(Json);
            note.Should().Be(ProxyResponseFilterNote.EmptyResult);
        }

        // ---- Failed --------------------------------------------------------

        [Theory]
        [InlineData(404)]
        [InlineData(500)]
        public void Failed_NonSuccessStatus(int status)
        {
            var (relay, ct, note, reason) = Project("""{"a":1}""", Json, status, "a");

            relay.Should().BeNull();
            ct.Should().BeNull();
            note.Should().Be(ProxyResponseFilterNote.Failed);
            reason.Should().Be($"upstream returned {status}");
        }

        [Theory]
        [InlineData("text/html", "text/html")]
        [InlineData("text/plain; charset=utf-8", "text/plain")]
        [InlineData(null, "none")]
        [InlineData("", "none")]
        public void Failed_NonJsonContentType(string? contentType, string expectedInReason)
        {
            var (relay, _, note, reason) = Project("""{"a":1}""", contentType, 200, "a");

            relay.Should().BeNull();
            note.Should().Be(ProxyResponseFilterNote.Failed);
            reason.Should().Be($"upstream response was not JSON ({expectedInReason})");
        }

        [Fact]
        public void Json_WithVendorSuffix_IsAccepted()
        {
            var (relay, _, note, _) = Project(
                """{"data":{"id":1}}""", "application/vnd.api+json", 200, "data.id");

            note.Should().Be(ProxyResponseFilterNote.Applied);
            Canonical(relay!).Should().Be("""{"data":{"id":1}}""");
        }

        [Fact]
        public void Failed_InvalidJson()
        {
            var (relay, _, note, reason) = Project("{ not json", Json, 200, "a");

            relay.Should().BeNull();
            note.Should().Be(ProxyResponseFilterNote.Failed);
            reason.Should().Be("upstream response could not be parsed as JSON");
        }

        [Fact]
        public void Failed_NestedOver128Levels()
        {
            var deep = new string('[', 200) + new string(']', 200);
            var (relay, _, note, _) = Project(deep, Json, 200, "a");

            relay.Should().BeNull();
            note.Should().Be(ProxyResponseFilterNote.Failed);
        }

        [Fact]
        public void Failed_BodyOverFiveMb()
        {
            var payload = "{\"a\":\"" + new string('x', 6 * 1024 * 1024) + "\"}";
            var (relay, _, note, reason) = Project(payload, Json, 200, "a");

            relay.Should().BeNull();
            note.Should().Be(ProxyResponseFilterNote.Failed);
            reason.Should().Contain("over the 5 MB filtering limit");
        }

        [Fact]
        public void Failed_ReasonNeverContainsUpstreamBodyText()
        {
            const string secret = "TOP-SECRET-TOKEN-9f8e7d";
            var body = $$"""{"token":"{{secret}}"}""";

            var (relay, _, note, reason) = Project(body, "text/html", 200, "token");

            relay.Should().BeNull();
            note.Should().Be(ProxyResponseFilterNote.Failed);
            reason.Should().NotContain(secret);
        }
    }
}
