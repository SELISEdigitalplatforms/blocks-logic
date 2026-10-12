using System.Net;
using FluentAssertions;
using Functions.DomainService.Entities;
using Functions.DomainService.Enums;
using Functions.DomainService.Models;
using Functions.DomainService.Services;
using Functions.DomainService.Utils;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

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

        /// <summary>Every host resolves to a documentation-range public address unless a test says otherwise.</summary>
        private static readonly IPAddress PublicAddress = IPAddress.Parse("93.184.216.34");

        private static OutputActionProcessor Processor(
            HttpMessageHandler handler, ISecretResolver? resolver = null,
            Func<string, IPAddress[]>? dns = null) => new(
                resolver ?? new FakeSecretResolver([]),
                new FakeHttpClientFactory(handler),
                new FunctionOutboundGuard((host, _) => Task.FromResult(dns?.Invoke(host) ?? [PublicAddress])),
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
        public async Task Every_request_carries_an_idempotency_key_of_runid_attempt_and_action()
        {
            var seen = new List<CapturedRequest>();
            var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK), seen);
            var processor = Processor(handler);

            await processor.ProcessAsync("tenant_1", Run(attempt: 3), [HttpAction()], new RetryPolicy());

            seen[0].Headers.Should().Contain(h => h.Name == "Idempotency-Key" && h.Value == "run_1-3-action_1");
        }

        [Fact]
        public async Task Two_actions_of_one_run_get_different_keys_so_a_receiver_keeps_both()
        {
            var seen = new List<CapturedRequest>();
            var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK), seen);
            var processor = Processor(handler);
            var second = HttpAction();
            second.Id = "action_2";

            await processor.ProcessAsync("tenant_1", Run(attempt: 1), [HttpAction(), second], new RetryPolicy());

            seen.Select(r => r.Headers.Single(h => h.Name == "Idempotency-Key").Value)
                .Should().Equal("run_1-1-action_1", "run_1-1-action_2");
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
            var handler = new StubHandler(_ => throw new HttpRequestException("Connection refused (10.0.0.5:6379)"), seen);
            var processor = Processor(handler);
            var policy = new RetryPolicy { Attempts = 2, Backoff = BackoffKind.Fixed, InitialDelaySeconds = 0 };

            var chain = await processor.ProcessAsync("tenant_1", Run(), [HttpAction()], policy);

            chain.AllSucceeded.Should().BeFalse();
            chain.Results[0].Error.Should().Be("connection failed after 2 attempt(s)");
            seen.Should().HaveCount(2);
        }

        // ---------------------------------------------------------------- SSRF ----

        [Theory]
        [InlineData("Connection refused (10.0.0.5:6379)")]
        [InlineData("No such host is known. (internal-redis.svc.cluster.local:6379)")]
        [InlineData("The SSL connection could not be established, see inner exception.")]
        public async Task A_network_error_never_echoes_the_exception_text(string message)
        {
            // The raw text is a port scanner: open vs closed vs filtered vs TLS answer each read
            // differently. The tenant sees one generic phrase; the detail is logged server-side.
            var handler = new StubHandler(_ => throw new HttpRequestException(message), []);
            var chain = await Processor(handler).ProcessAsync("tenant_1", Run(), [HttpAction()], new RetryPolicy { Attempts = 1 });

            chain.Results[0].Error.Should().Be("connection failed after 1 attempt(s)");
            chain.Results[0].Error.Should().NotContain("10.0.0.5").And.NotContain("6379").And.NotContain("SSL");
        }

        [Fact]
        public async Task A_timeout_is_a_reported_retryable_failure_not_an_escaping_cancellation()
        {
            // HttpClient's own timeout is a TaskCanceledException. Only the caller's token means "stop".
            var seen = new List<CapturedRequest>();
            var handler = new StubHandler(_ => throw new TaskCanceledException("The request was canceled due to the configured HttpClient.Timeout of 5 seconds elapsing.", new TimeoutException()), seen);
            var policy = new RetryPolicy { Attempts = 2, Backoff = BackoffKind.Fixed, InitialDelaySeconds = 0 };

            var chain = await Processor(handler).ProcessAsync("tenant_1", Run(), [HttpAction(timeoutSeconds: 5)], policy);

            chain.AllSucceeded.Should().BeFalse();
            chain.Results[0].Error.Should().Be("timed out (5s limit) after 2 attempt(s)");
            seen.Should().HaveCount(2);
        }

        [Fact]
        public async Task The_callers_own_cancellation_still_stops_the_chain()
        {
            using var cts = new CancellationTokenSource();
            var handler = new StubHandler(_ => { cts.Cancel(); throw new OperationCanceledException(cts.Token); }, []);

            var act = () => Processor(handler).ProcessAsync("tenant_1", Run(), [HttpAction()], new RetryPolicy(), cts.Token);

            await act.Should().ThrowAsync<OperationCanceledException>();
        }

        [Fact]
        public async Task A_secret_resolution_failure_is_reported_without_its_detail()
        {
            var resolver = new Mock<ISecretResolver>();
            resolver.Setup(r => r.ResolveAsync(It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new HttpRequestException("POST https://blocks-os.internal:5001/secrets failed"));
            var seen = new List<CapturedRequest>();
            var action = HttpAction(bodyTemplate: "{\"k\":\"{{secret.a}}\"}");

            var chain = await Processor(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK), seen), resolver.Object)
                .ProcessAsync("tenant_1", Run(), [action], new RetryPolicy());

            chain.Results[0].Error.Should().Be("could not prepare the request (secret resolution failed)");
            seen.Should().BeEmpty();
        }

        [Theory]
        [InlineData("http://127.0.0.1/hook")]
        [InlineData("http://10.1.2.3/hook")]
        [InlineData("http://172.16.0.1/hook")]
        [InlineData("http://192.168.1.1/hook")]
        [InlineData("http://169.254.169.254/latest/meta-data/")]
        [InlineData("http://[::1]/hook")]
        [InlineData("http://[fd00:ec2::254]/hook")]
        [InlineData("http://[fe80::1]/hook")]
        [InlineData("http://[::ffff:10.0.0.1]/hook")]
        [InlineData("http://2130706433/hook")]
        [InlineData("http://0177.0.0.1/hook")]
        [InlineData("http://0x7f.0.0.1/hook")]
        [InlineData("http://localhost:6379/")]
        [InlineData("http://metadata.google.internal/computeMetadata/v1/")]
        public async Task A_stored_action_pointing_inward_is_refused_at_send_and_never_sent(string url)
        {
            // Save-time validation is not enough on its own: versions saved before it existed are
            // still deployed, so the send path re-checks every time.
            var seen = new List<CapturedRequest>();
            var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK), seen);
            var policy = new RetryPolicy { Attempts = 3, Backoff = BackoffKind.Fixed, InitialDelaySeconds = 0 };

            var chain = await Processor(handler).ProcessAsync("tenant_1", Run(), [HttpAction(url: url)], policy);

            chain.AllSucceeded.Should().BeFalse();
            chain.Results[0].Error.Should().Be(FunctionOutboundGuard.BlockedMessage);
            chain.Results[0].Attempts.Should().Be(1, "a refusal is permanent and is not retried");
            seen.Should().BeEmpty();
        }

        [Theory]
        [InlineData("10.0.0.7")]
        [InlineData("127.0.0.1")]
        [InlineData("169.254.169.254")]
        [InlineData("::1")]
        [InlineData("::ffff:192.168.0.10")]
        [InlineData("100.100.100.200")]
        [InlineData("168.63.129.16")]
        public async Task A_public_name_that_resolves_inward_is_refused(string resolved)
        {
            var seen = new List<CapturedRequest>();
            var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK), seen);

            var chain = await Processor(handler, dns: _ => [IPAddress.Parse(resolved)])
                .ProcessAsync("tenant_1", Run(), [HttpAction(url: "https://hooks.example.com/x")], new RetryPolicy());

            chain.Results[0].Error.Should().Be(FunctionOutboundGuard.BlockedMessage);
            seen.Should().BeEmpty();
        }

        [Fact]
        public async Task A_name_with_one_public_and_one_private_record_is_refused()
        {
            // The classic rebinding setup: whichever record the connection picks is not ours to bet on.
            var seen = new List<CapturedRequest>();
            var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK), seen);

            var chain = await Processor(handler, dns: _ => [PublicAddress, IPAddress.Parse("10.0.0.1")])
                .ProcessAsync("tenant_1", Run(), [HttpAction(url: "https://hooks.example.com/x")], new RetryPolicy());

            chain.Results[0].Error.Should().Be(FunctionOutboundGuard.BlockedMessage);
            seen.Should().BeEmpty();
        }

        [Fact]
        public async Task A_host_supplied_by_a_secret_is_vetted_after_substitution()
        {
            var seen = new List<CapturedRequest>();
            var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK), seen);
            var resolver = new FakeSecretResolver(new Dictionary<string, string> { ["target"] = "169.254.169.254" });
            var action = HttpAction(url: "http://placeholder.example.com/x");
            action.Url = "http://{{secret.target}}/latest/meta-data/";

            var chain = await Processor(handler, resolver).ProcessAsync("tenant_1", Run(), [action], new RetryPolicy());

            chain.Results[0].Ok.Should().BeFalse();
            chain.Results[0].Error.Should().Be(FunctionOutboundGuard.BlockedMessage);
            seen.Should().BeEmpty();
        }

        [Fact]
        public async Task A_non_http_url_after_substitution_is_refused_without_sending()
        {
            var seen = new List<CapturedRequest>();
            var action = HttpAction();
            action.Url = "file:///etc/passwd";

            var chain = await Processor(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK), seen))
                .ProcessAsync("tenant_1", Run(), [action], new RetryPolicy());

            chain.Results[0].Ok.Should().BeFalse();
            chain.Results[0].Attempts.Should().Be(0);
            seen.Should().BeEmpty();
        }

        [Fact]
        public async Task A_redirect_is_reported_not_followed_and_not_retried()
        {
            var seen = new List<CapturedRequest>();
            var handler = new StubHandler(_ =>
            {
                var response = new HttpResponseMessage(HttpStatusCode.Found);
                response.Headers.Location = new Uri("http://169.254.169.254/latest/meta-data/");
                return response;
            }, seen);
            var policy = new RetryPolicy { Attempts = 3, Backoff = BackoffKind.Fixed, InitialDelaySeconds = 0 };

            var chain = await Processor(handler).ProcessAsync("tenant_1", Run(), [HttpAction()], policy);

            chain.AllSucceeded.Should().BeFalse();
            chain.Results[0].StatusCode.Should().Be(302);
            chain.Results[0].Error.Should().Contain("redirects are not followed");
            seen.Should().ContainSingle("the redirect target is never requested, and the same 302 is not retried");
        }

        [Fact]
        public async Task A_block_raised_by_the_connect_callback_is_reported_as_a_refusal()
        {
            // What SocketsHttpHandler surfaces when the ConnectCallback refuses: the guard's
            // exception wrapped in an HttpRequestException.
            var seen = new List<CapturedRequest>();
            var handler = new StubHandler(_ => throw new HttpRequestException(
                "wrapped", new OutboundTargetBlockedException(FunctionOutboundGuard.BlockedMessage)), seen);
            var policy = new RetryPolicy { Attempts = 3, Backoff = BackoffKind.Fixed, InitialDelaySeconds = 0 };

            var chain = await Processor(handler).ProcessAsync("tenant_1", Run(), [HttpAction()], policy);

            chain.Results[0].Error.Should().Be(FunctionOutboundGuard.BlockedMessage);
            seen.Should().ContainSingle();
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
