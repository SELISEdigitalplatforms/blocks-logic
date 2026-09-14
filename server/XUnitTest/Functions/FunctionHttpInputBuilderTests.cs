using System.Text;
using System.Text.Json;
using FluentAssertions;
using Functions.DomainService.Dtos.Requests;
using Functions.DomainService.Enums;
using Functions.DomainService.Models;
using Functions.DomainService.Services;

namespace XUnitTest.Functions
{
    /// <summary>
    /// What a handler receives as <c>input</c> for an HTTP call. Two things matter most: that the
    /// shape is exactly the documented one, and that no header able to carry a credential ever
    /// gets in — the envelope screen would refuse the run, but the builder must not rely on that.
    /// </summary>
    public class FunctionHttpInputBuilderTests
    {
        private static InvokeFunctionRequestDto Request(
            string method = "POST", string? path = "orders/42", string? body = "{\"qty\":2}",
            string? contentType = "application/json",
            Dictionary<string, string[]>? query = null,
            Dictionary<string, string>? headers = null) => new()
        {
            Method = method,
            Path = path,
            Body = body is null ? null : Encoding.UTF8.GetBytes(body),
            ContentType = contentType,
            Query = query ?? new Dictionary<string, string[]>(),
            Headers = headers ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
        };

        private static JsonElement Build(InvokeFunctionRequestDto request) =>
            JsonDocument.Parse(FunctionHttpInputBuilder.Build(request)).RootElement.Clone();

        [Fact]
        public void Produces_the_documented_shape()
        {
            var root = Build(Request(
                method: "post",
                query: new() { ["limit"] = ["10"], ["tag"] = ["a", "b"] },
                headers: new(StringComparer.OrdinalIgnoreCase) { ["Content-Type"] = "application/json", ["User-Agent"] = "curl" }));

            root.GetProperty("method").GetString().Should().Be("POST");
            root.GetProperty("path").GetString().Should().Be("orders/42");
            root.GetProperty("query").GetProperty("limit").GetString().Should().Be("10");
            root.GetProperty("query").GetProperty("tag").EnumerateArray().Select(e => e.GetString()).Should().Equal("a", "b");
            root.GetProperty("headers").GetProperty("content-type").GetString().Should().Be("application/json");
            root.GetProperty("headers").GetProperty("user-agent").GetString().Should().Be("curl");
            root.GetProperty("body").GetProperty("qty").GetInt32().Should().Be(2);
        }

        [Theory]
        [InlineData("Authorization")]
        [InlineData("Cookie")]
        [InlineData("x-blocks-key")]
        [InlineData("X-Api-Key")]
        [InlineData("Proxy-Authorization")]
        public void Credential_bearing_headers_never_reach_the_handler(string header)
        {
            var root = Build(Request(headers: new(StringComparer.OrdinalIgnoreCase)
            {
                [header] = "secret-value",
                ["Accept"] = "application/json",
            }));

            root.GetProperty("headers").EnumerateObject().Select(p => p.Name).Should().Equal("accept");
            FunctionHttpInputBuilder.Build(Request(headers: new(StringComparer.OrdinalIgnoreCase) { [header] = "x" }))
                .Should().NotContain("secret-value").And.NotContainEquivalentOf(header);
        }

        [Fact]
        public void The_shaped_input_passes_the_envelope_screen_even_with_a_full_browser_header_set()
        {
            // If the allow-list ever admits something credential-shaped, this is the test that
            // catches it: the screen is the same one the envelope builder runs before enqueue.
            var json = FunctionHttpInputBuilder.Build(Request(headers: new(StringComparer.OrdinalIgnoreCase)
            {
                ["Authorization"] = "Bearer t", ["Cookie"] = "s=1", ["x-blocks-key"] = "k",
                ["Accept"] = "*/*", ["Accept-Language"] = "en", ["User-Agent"] = "ua", ["Origin"] = "https://a",
                ["Referer"] = "https://a/b", ["X-Request-Id"] = "r1", ["X-Forwarded-For"] = "1.2.3.4",
            }));

            var act = () => FunctionEnvelopeBuilder.Screen(json);
            act.Should().NotThrow();
        }

        [Fact]
        public void A_credential_shaped_query_key_is_left_for_the_screen_to_refuse()
        {
            // Query keys are the caller's own and pass through; the envelope screen then refuses
            // the run, which the controller answers as 400 rather than delivering a token.
            var json = FunctionHttpInputBuilder.Build(Request(query: new() { ["api_key"] = ["abc"] }));

            var act = () => FunctionEnvelopeBuilder.Screen(json);
            act.Should().Throw<FunctionEnvelopeBuilder.ForbiddenContentException>().WithMessage("*api_key*");
        }

        [Theory]
        [InlineData(null, "")]
        [InlineData("", "")]
        [InlineData("/", "")]
        [InlineData("orders//42/", "orders/42")]
        [InlineData("/a/b", "a/b")]
        public void Path_is_normalised_so_handlers_can_compare_literally(string? path, string expected)
        {
            Build(Request(path: path)).GetProperty("path").GetString().Should().Be(expected);
        }

        [Fact]
        public void Json_bodies_arrive_parsed_and_text_bodies_as_text()
        {
            Build(Request(body: "{\"a\":1}", contentType: "application/json; charset=utf-8"))
                .GetProperty("body").GetProperty("a").GetInt32().Should().Be(1);

            Build(Request(body: "hello", contentType: "text/plain"))
                .GetProperty("body").GetString().Should().Be("hello");

            // Undeclared content type, JSON-looking payload: curl without -H still gets an object.
            Build(Request(body: "[1,2]", contentType: null))
                .GetProperty("body").EnumerateArray().Should().HaveCount(2);
        }

        [Fact]
        public void A_body_that_claims_json_but_is_not_is_delivered_as_the_raw_string()
        {
            Build(Request(body: "{not json", contentType: "application/json"))
                .GetProperty("body").GetString().Should().Be("{not json");
        }

        [Fact]
        public void No_body_is_an_explicit_null()
        {
            Build(Request(method: "GET", body: null, contentType: null))
                .GetProperty("body").ValueKind.Should().Be(JsonValueKind.Null);
        }

        [Fact]
        public void A_test_run_is_shaped_like_a_POST_to_the_root_with_the_payload_as_body()
        {
            var root = JsonDocument.Parse(FunctionHttpInputBuilder.ForTest("{\"orderId\":\"7\"}")).RootElement;

            root.GetProperty("method").GetString().Should().Be("POST");
            root.GetProperty("path").GetString().Should().Be("");
            root.GetProperty("query").EnumerateObject().Should().BeEmpty();
            root.GetProperty("headers").GetProperty("content-type").GetString().Should().Be("application/json");
            root.GetProperty("body").GetProperty("orderId").GetString().Should().Be("7");
        }

        [Fact]
        public void A_test_run_of_a_GET_function_puts_the_payload_in_the_query_and_sends_no_body()
        {
            // A real GET has no body, so testing one with the payload as body would exercise code
            // that production never runs. The payload's fields become the query string instead.
            var root = JsonDocument.Parse(FunctionHttpInputBuilder.ForTest(
                "{\"orderId\":\"7\",\"limit\":10,\"tag\":[\"a\",\"b\"],\"deep\":{\"x\":1}}",
                HttpTriggerMethod.Get)).RootElement;

            root.GetProperty("method").GetString().Should().Be("GET");
            root.GetProperty("body").ValueKind.Should().Be(JsonValueKind.Null);
            root.GetProperty("headers").TryGetProperty("content-type", out _).Should().BeFalse();
            var query = root.GetProperty("query");
            query.GetProperty("orderId").GetString().Should().Be("7");
            query.GetProperty("limit").GetString().Should().Be("10");
            query.GetProperty("tag").EnumerateArray().Select(e => e.GetString()).Should().Equal("a", "b");
            query.GetProperty("deep").GetString().Should().Be("{\"x\":1}");
        }

        [Fact]
        public void A_non_object_payload_has_nowhere_to_go_on_a_GET()
        {
            JsonDocument.Parse(FunctionHttpInputBuilder.ForTest("[1,2]", HttpTriggerMethod.Get)).RootElement
                .GetProperty("query").EnumerateObject().Should().BeEmpty();
        }

        [Fact]
        public void The_default_trigger_method_is_POST_so_older_functions_keep_behaving()
        {
            new TriggerConfig().HttpMethod.Should().Be(HttpTriggerMethod.Post);
            FunctionHttpInputBuilder.Verb(HttpTriggerMethod.Post).Should().Be("POST");
            FunctionHttpInputBuilder.Verb(HttpTriggerMethod.Get).Should().Be("GET");
        }

        [Fact]
        public void An_empty_test_payload_is_a_null_body_not_a_missing_one()
        {
            JsonDocument.Parse(FunctionHttpInputBuilder.ForTest(null)).RootElement
                .GetProperty("body").ValueKind.Should().Be(JsonValueKind.Null);
        }

        [Fact]
        public void The_body_cap_leaves_room_for_the_rest_of_the_envelope()
        {
            FunctionHttpInputBuilder.MaxBodyBytes.Should().BeLessThan(FunctionLimits.Ceiling.InputBytes);
            FunctionHttpInputBuilder.MaxBodyBytes.Should().BeGreaterThan(FunctionLimits.Ceiling.InputBytes / 2);
        }
    }
}
