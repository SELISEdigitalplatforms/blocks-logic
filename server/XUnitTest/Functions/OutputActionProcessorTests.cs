using System.Net;
using FluentAssertions;
using Functions.DomainService.Entities;
using Functions.DomainService.Enums;
using Functions.DomainService.Models;
using Functions.DomainService.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace XUnitTest.Functions
{
    /// <summary>
    /// <see cref="OutputActionProcessor"/> is the only place a run's result leaves the
    /// platform, so these tests weigh toward what happens when things go wrong: a chain that
    /// stops at its first failure, retries that reuse one idempotency key, and secret
    /// substitution that never lets a stale reference silently disappear from the request.
    /// </summary>
    public class OutputActionProcessorTests
    {
        private sealed record CapturedRequest(
            HttpMethod Method, Uri? Uri, string? Body, List<(string Name, string Value)> Headers);

        private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond, List<CapturedRequest> seen)
            : HttpMessageHandler
        {
            protected override async Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request, CancellationToken cancellationToken)
            {
                var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
                var headers = request.Headers.SelectMany(h => h.Value.Select(v => (h.Key, v))).ToList();
                seen.Add(new CapturedRequest(request.Method, request.RequestUri, body, headers));
                return respond(request);
            }
        }

        private sealed class FakeHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
        {
            public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
        }

        private sealed class FakeSecretResolver(Dictionary<string, string> values) : ISecretResolver
        {
            public List<IReadOnlyCollection<string>> Requests { get; } = [];

            public Task<IReadOnlyDictionary<string, string>> ResolveAsync(
                IReadOnlyCollection<string> secretIds, string tenantId, CancellationToken cancellationToken = default)
            {
                Requests.Add(secretIds);
                var found = secretIds.Where(values.ContainsKey).ToDictionary(id => id, id => values[id]);
                return Task.FromResult<IReadOnlyDictionary<string, string>>(found);
            }
        }

        private static FunctionRunEntity Run(int attempt = 1, string result = "{\"ok\":true}") => new()
        {
            ItemId = "run_1",
            FunctionId = "fn_1",
            Attempt = attempt,
            Result = result,
        };

        private static OutputAction HttpAction(
            string url = "https://example.invalid/hook",
            string? bodyTemplate = null,
            Dictionary<string, string>? headers = null,
            int timeoutSeconds = 5) => new()
        {
            Id = "action_1",
            Kind = OutputActionKind.ExternalHttp,
            Enabled = true,
            Url = url,
            Method = "POST",
            BodyTemplate = bodyTemplate,
            Headers = headers ?? [],
            TimeoutSeconds = timeoutSeconds,
        };

        private static OutputActionProcessor Processor(
            HttpMessageHandler handler, ISecretResolver? resolver = null) => new(
                resolver ?? new FakeSecretResolver([]),
                new FakeHttpClientFactory(handler),
                NullLogger<OutputActionProcessor>.Instance);

        [Fact]
        public async Task A_successful_action_reports_ok_with_the_status_code()
        {
            var seen = new List<CapturedRequest>();
            var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK), seen);
            var processor = Processor(handler);

            var chain = await processor.ProcessAsync("tenant_1", Run(), [HttpAction()], new RetryPolicy());

            chain.AllSucceeded.Should().BeTrue();
            chain.Results.Should().ContainSingle();
            chain.Results[0].Ok.Should().BeTrue();
            chain.Results[0].StatusCode.Should().Be(200);
        }

        [Fact]
        public async Task The_body_defaults_to_the_runs_raw_result_when_no_template_is_set()
        {
            var seen = new List<CapturedRequest>();
            var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK), seen);
            var processor = Processor(handler);

            await processor.ProcessAsync("tenant_1", Run(result: "{\"total\":42}"), [HttpAction()], new RetryPolicy());

            seen.Should().ContainSingle();
            seen[0].Body.Should().Be("{\"total\":42}");
        }

        [Fact]
        public async Task A_body_template_substitutes_the_result_placeholder()
        {
            var seen = new List<CapturedRequest>();
            var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK), seen);
            var processor = Processor(handler);
            var action = HttpAction(bodyTemplate: "{\"wrapped\":{{result}}}");

            await processor.ProcessAsync("tenant_1", Run(result: "1"), [action], new RetryPolicy());

            seen[0].Body.Should().Be("{\"wrapped\":1}");
        }

        [Fact]
        public async Task Every_request_carries_an_idempotency_key_of_runid_dash_attempt()
        {
            var seen = new List<CapturedRequest>();
            var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK), seen);
            var processor = Processor(handler);

            await processor.ProcessAsync("tenant_1", Run(attempt: 3), [HttpAction()], new RetryPolicy());

            seen[0].Headers.Should().Contain(h => h.Name == "Idempotency-Key" && h.Value == "run_1-3");
        }

        [Fact]
        public async Task Secrets_referenced_in_the_url_headers_and_body_are_all_substituted()
        {
            var seen = new List<CapturedRequest>();
            var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK), seen);
            var resolver = new FakeSecretResolver(new Dictionary<string, string>
            {
                ["sec-key"] = "sk_live_abc",
                ["sec-sig"] = "signing-value",
            });
            var processor = Processor(handler, resolver);
            var action = HttpAction(
                url: "https://example.invalid/hook?key={{secret.sec-key}}",
                headers: new Dictionary<string, string> { ["X-Signature"] = "{{secret.sec-sig}}" },
                bodyTemplate: "{\"k\":\"{{secret.sec-key}}\"}");

            await processor.ProcessAsync("tenant_1", Run(), [action], new RetryPolicy());

            seen[0].Uri!.Query.Should().Contain("sk_live_abc");
            seen[0].Headers.Should().Contain(h => h.Name == "X-Signature" && h.Value == "signing-value");
            seen[0].Body.Should().Contain("sk_live_abc");
        }

        [Fact]
        public async Task An_unresolvable_secret_reference_is_left_visibly_unresolved_not_silently_dropped()
        {
            // A stale reference must be obvious in the outgoing request (so it fails loudly
            // against the receiving endpoint) rather than quietly vanishing into an empty string.
            var seen = new List<CapturedRequest>();
            var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK), seen);
            var processor = Processor(handler, new FakeSecretResolver([]));
            var action = HttpAction(bodyTemplate: "{\"key\":\"{{secret.gone}}\"}");

            await processor.ProcessAsync("tenant_1", Run(), [action], new RetryPolicy());

            seen[0].Body.Should().Contain("{{secret.gone}}");
        }

        [Fact]
        public async Task Only_the_secret_ids_actually_referenced_are_resolved()
        {
            var seen = new List<CapturedRequest>();
            var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK), seen);
            var resolver = new FakeSecretResolver(new Dictionary<string, string> { ["a"] = "1" });
            var processor = Processor(handler, resolver);
            var action = HttpAction(bodyTemplate: "{\"a\":\"{{secret.a}}\"}");

            await processor.ProcessAsync("tenant_1", Run(), [action], new RetryPolicy());

            resolver.Requests.Should().ContainSingle();
            resolver.Requests[0].Should().BeEquivalentTo(["a"]);
        }

        [Fact]
        public async Task A_failing_action_is_retried_up_to_the_policy_and_then_reported()
        {
            var seen = new List<CapturedRequest>();
            var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError), seen);
            var processor = Processor(handler);
            var policy = new RetryPolicy { Attempts = 3, Backoff = BackoffKind.Fixed, InitialDelaySeconds = 0 };

            var chain = await processor.ProcessAsync("tenant_1", Run(), [HttpAction()], policy);

            seen.Should().HaveCount(3);
            chain.AllSucceeded.Should().BeFalse();
            chain.Results[0].Attempts.Should().Be(3);
            chain.Results[0].StatusCode.Should().Be(500);
        }

        [Fact]
        public async Task A_later_success_within_the_retry_budget_stops_retrying()
        {
            var seen = new List<CapturedRequest>();
            var attempt = 0;
            var handler = new StubHandler(_ =>
            {
                attempt++;
                return new HttpResponseMessage(attempt < 2 ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK);
            }, seen);
            var processor = Processor(handler);
            var policy = new RetryPolicy { Attempts = 3, Backoff = BackoffKind.Fixed, InitialDelaySeconds = 0 };

            var chain = await processor.ProcessAsync("tenant_1", Run(), [HttpAction()], policy);

            chain.AllSucceeded.Should().BeTrue();
            chain.Results[0].Attempts.Should().Be(2);
            seen.Should().HaveCount(2);
        }

        [Fact]
        public async Task A_network_exception_is_retried_the_same_way_as_a_bad_status()
        {
            var seen = new List<CapturedRequest>();
            var handler = new StubHandler(_ => throw new HttpRequestException("connection refused"), seen);
            var processor = Processor(handler);
            var policy = new RetryPolicy { Attempts = 2, Backoff = BackoffKind.Fixed, InitialDelaySeconds = 0 };

            var chain = await processor.ProcessAsync("tenant_1", Run(), [HttpAction()], policy);

            chain.AllSucceeded.Should().BeFalse();
            chain.Results[0].Error.Should().Contain("connection refused");
            seen.Should().HaveCount(2);
        }

        [Fact]
        public async Task The_chain_stops_at_the_first_failure_and_does_not_run_later_actions()
        {
            var seen = new List<CapturedRequest>();
            var handler = new StubHandler(request =>
                request.RequestUri!.AbsoluteUri.Contains("first")
                    ? new HttpResponseMessage(HttpStatusCode.InternalServerError)
                    : new HttpResponseMessage(HttpStatusCode.OK), seen);
            var processor = Processor(handler);
            var actions = new List<OutputAction>
            {
                HttpAction(url: "https://example.invalid/first"),
                HttpAction(url: "https://example.invalid/second"),
            };
            actions[0].Id = "first";
            actions[1].Id = "second";

            var chain = await processor.ProcessAsync("tenant_1", Run(), actions, new RetryPolicy { Attempts = 1 });

            chain.AllSucceeded.Should().BeFalse();
            chain.Results.Should().ContainSingle().Which.ActionId.Should().Be("first");
            seen.Should().ContainSingle();
        }

        [Fact]
        public async Task A_disabled_action_is_skipped_entirely()
        {
            var seen = new List<CapturedRequest>();
            var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK), seen);
            var processor = Processor(handler);
            var action = HttpAction();
            action.Enabled = false;

            var chain = await processor.ProcessAsync("tenant_1", Run(), [action], new RetryPolicy());

            chain.AllSucceeded.Should().BeTrue();
            chain.Results.Should().BeEmpty();
            seen.Should().BeEmpty();
        }

        [Fact]
        public async Task A_blocks_proxy_action_is_skipped_rather_than_attempted()
        {
            var seen = new List<CapturedRequest>();
            var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK), seen);
            var processor = Processor(handler);
            var action = HttpAction();
            action.Kind = OutputActionKind.BlocksProxy;

            var chain = await processor.ProcessAsync("tenant_1", Run(), [action], new RetryPolicy());

            chain.AllSucceeded.Should().BeTrue();
            chain.Results.Should().BeEmpty();
            seen.Should().BeEmpty();
        }

        [Fact]
        public async Task No_actions_at_all_is_a_successful_empty_chain()
        {
            var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK), []);
            var processor = Processor(handler);

            var chain = await processor.ProcessAsync("tenant_1", Run(), [], new RetryPolicy());

            chain.AllSucceeded.Should().BeTrue();
            chain.Results.Should().BeEmpty();
        }
    }
}
