using FluentAssertions;
using Proxy.DomainService.Dtos;
using Proxy.DomainService.Entities;
using Proxy.DomainService.Utils;

namespace XUnitTest.Proxy
{
    /// <summary>
    /// Timeout, retry and breaker settings.
    /// <para>
    /// The rule underneath every case here is that <b>nothing is defaulted</b>: a proxy that does not
    /// configure resilience must behave exactly as it did before the setting existed, and a tenant who
    /// wants a retry has to say so — including saying that repeating the request is safe.
    /// </para>
    /// </summary>
    public class ProxyResilienceTests
    {
        private static ProxyConfigValidationResult Normalize(
            ProxyResilienceInputDto? input, out ProxyResilienceConfig? config)
        {
            var result = new ProxyConfigValidationResult();
            config = ProxyConfigValidator.NormalizeResilience(input, "route 'GET /x'", "routes", result);
            return result;
        }

        // ---- nothing configured stays nothing -----------------------------------------

        [Fact]
        public void No_resilience_at_all_stores_nothing()
        {
            Normalize(null, out var config).IsValid.Should().BeTrue();
            config.Should().BeNull();
        }

        [Fact]
        public void An_object_that_asks_for_nothing_is_stored_as_nothing()
        {
            // Saving through a form that did not fill these in must be indistinguishable from never
            // having configured them, or a round trip through the UI would quietly change behaviour.
            Normalize(new ProxyResilienceInputDto(), out var config).IsValid.Should().BeTrue();
            config.Should().BeNull();
        }

        [Fact]
        public void One_attempt_is_not_a_retry_policy()
        {
            // Attempts = 1 means "send it once", which is the absence of a policy, not a policy.
            var result = Normalize(
                new ProxyResilienceInputDto { Retry = new ProxyRetryInputDto { Attempts = 1 } }, out var config);

            result.IsValid.Should().BeTrue();
            config.Should().BeNull();
        }

        // ---- the rule that stops a double charge --------------------------------------

        [Fact]
        public void Retries_are_refused_without_an_explicit_idempotency_claim()
        {
            var result = Normalize(
                new ProxyResilienceInputDto { Retry = new ProxyRetryInputDto { Attempts = 3 } }, out var config);

            result.IsValid.Should().BeFalse();
            result.Errors["routes"].Should().Contain("idempotent");
            config.Should().BeNull();
        }

        [Fact]
        public void Idempotent_false_is_still_a_refusal()
        {
            // Explicitly false must fail the same way as absent. A tenant who ticked and then unticked
            // the box has said no.
            var result = Normalize(
                new ProxyResilienceInputDto
                {
                    Retry = new ProxyRetryInputDto { Attempts = 2, Idempotent = false },
                },
                out _);

            result.IsValid.Should().BeFalse();
        }

        [Fact]
        public void Retries_are_accepted_once_the_tenant_says_repeating_is_safe()
        {
            var result = Normalize(
                new ProxyResilienceInputDto
                {
                    Retry = new ProxyRetryInputDto
                    {
                        Attempts = 3, Idempotent = true, Backoff = "Exponential", InitialDelaySeconds = 2,
                    },
                },
                out var config);

            result.IsValid.Should().BeTrue();
            config!.Retry!.Attempts.Should().Be(3);
            config.Retry.Backoff.Should().Be(ProxyBackoffKind.Exponential);
            config.Retry.InitialDelaySeconds.Should().Be(2);
            config.Retry.Idempotent.Should().BeTrue();
        }

        // ---- bounds --------------------------------------------------------------------

        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        [InlineData(ProxyConfigValidator.MaxTimeoutSeconds + 1)]
        public void A_timeout_outside_the_ceiling_is_refused(int seconds)
        {
            // The ceiling is not a product opinion: the gateway holds a connection and a request thread
            // for the whole forward, so an unbounded value is a denial of service against this host.
            Normalize(new ProxyResilienceInputDto { TimeoutSeconds = seconds }, out _)
                .IsValid.Should().BeFalse();
        }

        [Fact]
        public void A_timeout_inside_the_ceiling_is_kept_exactly()
        {
            Normalize(new ProxyResilienceInputDto { TimeoutSeconds = 7 }, out var config)
                .IsValid.Should().BeTrue();

            config!.TimeoutSeconds.Should().Be(7, "the tenant's number is not adjusted for them");
        }

        [Theory]
        [InlineData(0, 30)]
        [InlineData(5, 0)]
        [InlineData(ProxyConfigValidator.MaxBreakerThreshold + 1, 30)]
        [InlineData(5, ProxyConfigValidator.MaxBreakerOpenSeconds + 1)]
        public void Breaker_settings_outside_their_bounds_are_refused(int threshold, int openSeconds)
        {
            Normalize(
                new ProxyResilienceInputDto
                {
                    Breaker = new ProxyBreakerInputDto { FailureThreshold = threshold, OpenSeconds = openSeconds },
                },
                out _).IsValid.Should().BeFalse();
        }

        [Fact]
        public void An_unknown_backoff_is_refused_rather_than_guessed()
        {
            Normalize(
                new ProxyResilienceInputDto
                {
                    Retry = new ProxyRetryInputDto { Attempts = 2, Idempotent = true, Backoff = "Sideways" },
                },
                out _).IsValid.Should().BeFalse();
        }

        // ---- the breaker ---------------------------------------------------------------

        private sealed class Clock : TimeProvider
        {
            public DateTimeOffset Now = DateTimeOffset.UnixEpoch;
            public override DateTimeOffset GetUtcNow() => Now;
        }

        private static readonly ProxyBreakerConfig Breaker = new() { FailureThreshold = 3, OpenSeconds = 30 };

        [Fact]
        public void The_circuit_opens_only_after_the_threshold()
        {
            var clock = new Clock();
            var breaker = new ProxyCircuitBreaker(clock);

            breaker.IsOpen("t1", "api.example.com", Breaker).Should().BeFalse("nothing has failed yet");

            breaker.RecordFailure("t1", "api.example.com", Breaker);
            breaker.RecordFailure("t1", "api.example.com", Breaker);
            breaker.IsOpen("t1", "api.example.com", Breaker).Should().BeFalse("two is under the threshold");

            breaker.RecordFailure("t1", "api.example.com", Breaker);
            breaker.IsOpen("t1", "api.example.com", Breaker).Should().BeTrue();
        }

        [Fact]
        public void A_success_closes_it_again()
        {
            var breaker = new ProxyCircuitBreaker(new Clock());

            for (var i = 0; i < 3; i++) breaker.RecordFailure("t1", "api.example.com", Breaker);
            breaker.RecordSuccess("t1", "api.example.com");

            breaker.IsOpen("t1", "api.example.com", Breaker).Should().BeFalse();
        }

        [Fact]
        public void After_the_open_window_exactly_one_probe_is_let_through()
        {
            // Half-open. Letting everyone through at once would hit a recovering upstream with the whole
            // backlog the moment it came back.
            var clock = new Clock();
            var breaker = new ProxyCircuitBreaker(clock);

            for (var i = 0; i < 3; i++) breaker.RecordFailure("t1", "api.example.com", Breaker);
            clock.Now = clock.Now.AddSeconds(31);

            breaker.IsOpen("t1", "api.example.com", Breaker).Should().BeFalse("this caller is the probe");
            breaker.IsOpen("t1", "api.example.com", Breaker).Should().BeTrue("everyone else still waits");
        }

        [Fact]
        public void One_tenants_broken_vendor_does_not_break_it_for_another()
        {
            var breaker = new ProxyCircuitBreaker(new Clock());

            for (var i = 0; i < 3; i++) breaker.RecordFailure("t1", "api.example.com", Breaker);

            breaker.IsOpen("t1", "api.example.com", Breaker).Should().BeTrue();
            breaker.IsOpen("t2", "api.example.com", Breaker).Should().BeFalse();
        }

        // ---- what the history shows, and what a revert restores ------------------------

        private static ProxyConfigSnapshot Snapshot(ProxyResilienceConfig? resilience) => new()
        {
            Name = "p", Slug = "p", Upstream = "https://api.example.com", Resilience = resilience,
        };

        [Fact]
        public void Turning_retries_on_reads_as_a_sentence_in_the_history()
        {
            // Somebody reading the version history wants the behaviour change, not a serialized object.
            var changes = ProxyChangeSet.Diff(
                Snapshot(null),
                Snapshot(new ProxyResilienceConfig
                {
                    Retry = new ProxyRetryConfig
                    {
                        Attempts = 3, Backoff = ProxyBackoffKind.Exponential,
                        InitialDelaySeconds = 1, Idempotent = true,
                    },
                }));

            var row = changes.Single(c => c.Label == "retries");
            row.Before.Should().Be("off");
            row.After.Should().Be("3 attempts, backing off from 1s");
        }

        [Fact]
        public void An_unset_timeout_reads_as_not_set_rather_than_as_a_number()
        {
            // Showing a number would tell the reader a value was chosen when none was — and an unset
            // timeout is the state that inherits.
            var changes = ProxyChangeSet.Diff(
                Snapshot(null), Snapshot(new ProxyResilienceConfig { TimeoutSeconds = 12 }));

            var row = changes.Single(c => c.Label == "timeout");
            row.Before.Should().Be("not set");
            row.After.Should().Be("12s");
        }

        [Fact]
        public void Changing_a_timeout_does_not_report_the_retry_as_changed()
        {
            // Three decisions, three rows. Collapsing them would hide which one someone actually made.
            var retry = new ProxyRetryConfig { Attempts = 2, Idempotent = true };

            var changes = ProxyChangeSet.Diff(
                Snapshot(new ProxyResilienceConfig { TimeoutSeconds = 10, Retry = retry }),
                Snapshot(new ProxyResilienceConfig { TimeoutSeconds = 20, Retry = retry }));

            changes.Should().ContainSingle().Which.Label.Should().Be("timeout");
        }

        [Fact]
        public void A_route_timeout_change_is_a_visible_change()
        {
            // Without this the save produced a version with no diff rows, which reads as "nothing
            // happened" — and the update path skips writing a version at all when nothing changed.
            var before = new ProxyRouteConfig { Method = HttpMethodType.Get, Path = "a" };
            var after = new ProxyRouteConfig
            {
                Method = HttpMethodType.Get, Path = "a",
                Resilience = new ProxyResilienceConfig { TimeoutSeconds = 5 },
            };

            ProxyRouteCodec.OverridesEqual(before, after).Should().BeFalse();
        }

        [Fact]
        public void A_route_resilience_survives_the_round_trip_a_revert_uses()
        {
            // Revert decodes a stored row. Dropping resilience here would silently wipe it on revert.
            var route = new ProxyRouteConfig
            {
                Method = HttpMethodType.Get,
                Path = "orders/{id}",
                Resilience = new ProxyResilienceConfig
                {
                    TimeoutSeconds = 9,
                    Retry = new ProxyRetryConfig { Attempts = 2, Idempotent = true, InitialDelaySeconds = 3 },
                    Breaker = new ProxyBreakerConfig { FailureThreshold = 4, OpenSeconds = 20 },
                },
            };

            var restored = ProxyRouteCodec.Decode(
                ProxyRouteCodec.AddressOf(route), ProxyRouteCodec.Encode(route));

            restored!.Resilience!.TimeoutSeconds.Should().Be(9);
            restored.Resilience.Retry!.Attempts.Should().Be(2);
            restored.Resilience.Breaker!.FailureThreshold.Should().Be(4);
        }

        [Fact]
        public void A_restored_retry_without_the_idempotency_claim_is_dropped()
        {
            // A history row can predate the rule, or be edited in the database, and revert does not
            // re-run the validator. The tenant's assertion that repeating is safe is not inferable.
            const string smuggled =
                "{\"resilience\":{\"retry\":{\"attempts\":3,\"initialDelaySeconds\":1,\"idempotent\":false}}}";

            ProxyRouteCodec.Decode("GET orders", smuggled)!.Resilience.Should().BeNull();
        }

        [Fact]
        public void A_restored_value_outside_todays_bounds_is_dropped_not_installed()
        {
            const string smuggled = "{\"resilience\":{\"timeoutSeconds\":99999}}";

            ProxyRouteCodec.Decode("GET orders", smuggled)!.Resilience.Should().BeNull();
        }

        [Fact]
        public void Hosts_are_counted_separately()
        {
            var breaker = new ProxyCircuitBreaker(new Clock());

            for (var i = 0; i < 3; i++) breaker.RecordFailure("t1", "a.example.com", Breaker);

            breaker.IsOpen("t1", "a.example.com", Breaker).Should().BeTrue();
            breaker.IsOpen("t1", "b.example.com", Breaker).Should().BeFalse();
        }
    }
}
