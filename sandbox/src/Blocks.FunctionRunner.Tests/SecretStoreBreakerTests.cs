using Blocks.FunctionRunner.SecretStore;
using FluentAssertions;
using Xunit;

namespace Blocks.FunctionRunner.Tests
{
    /// <summary>
    /// Secrets are resolved after a run has taken three slots, on purpose — the plaintext should
    /// live as briefly and as close to the sandbox as possible. The cost of that ordering appears
    /// when the store is down, and this is what stops it draining a host.
    /// </summary>
    public class SecretStoreBreakerTests
    {
        /// <summary>
        /// A hand-rolled clock, as elsewhere in this project: there is no mocking library and no
        /// FakeTimeProvider package, and only <c>GetUtcNow</c> is needed here.
        /// </summary>
        private sealed class StubTime : TimeProvider
        {
            private DateTimeOffset _now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

            public override DateTimeOffset GetUtcNow() => _now;

            public void Advance(TimeSpan by) => _now = _now.Add(by);
        }

        private static (SecretStoreBreaker Breaker, StubTime Clock) Build()
        {
            var clock = new StubTime();
            return (new SecretStoreBreaker(clock), clock);
        }

        [Fact]
        public void A_healthy_store_is_never_skipped()
        {
            Build().Breaker.ShouldSkip("t1").Should().BeFalse();
        }

        /// <summary>
        /// One failure is enough. Unlike a flaky upstream, a secret store is either there or it is
        /// not — and every attempt costs three slots held for the whole timeout.
        /// </summary>
        [Fact]
        public void One_failure_is_enough_to_stop_asking()
        {
            var (breaker, _) = Build();

            breaker.RecordUnavailable("t1");

            breaker.ShouldSkip("t1").Should().BeTrue();
        }

        /// <summary>
        /// One tenant's vault being unreachable says nothing about another's, and must not stop
        /// every other tenant's functions from running.
        /// </summary>
        [Fact]
        public void One_tenants_outage_does_not_stop_another_tenant()
        {
            var (breaker, _) = Build();

            breaker.RecordUnavailable("t1");

            breaker.ShouldSkip("t2").Should().BeFalse();
        }

        [Fact]
        public void After_the_window_exactly_one_run_is_let_through_to_find_out()
        {
            var (breaker, clock) = Build();
            breaker.RecordUnavailable("t1");

            clock.Advance(SecretStoreBreaker.OpenWindow + TimeSpan.FromSeconds(1));

            breaker.ShouldSkip("t1").Should().BeFalse("one probe goes through");
            breaker.ShouldSkip("t1").Should().BeTrue("and the rest keep waiting for it to report");
        }

        [Fact]
        public void A_probe_that_succeeds_reopens_the_store_for_everyone()
        {
            var (breaker, clock) = Build();
            breaker.RecordUnavailable("t1");
            clock.Advance(SecretStoreBreaker.OpenWindow + TimeSpan.FromSeconds(1));
            breaker.ShouldSkip("t1");                       // the probe

            breaker.RecordSuccess("t1");

            breaker.ShouldSkip("t1").Should().BeFalse();
            breaker.ShouldSkip("t1").Should().BeFalse();
        }

        [Fact]
        public void A_probe_that_fails_closes_it_again_for_a_fresh_window()
        {
            var (breaker, clock) = Build();
            breaker.RecordUnavailable("t1");
            clock.Advance(SecretStoreBreaker.OpenWindow + TimeSpan.FromSeconds(1));
            breaker.ShouldSkip("t1");                       // the probe

            breaker.RecordUnavailable("t1");

            breaker.ShouldSkip("t1").Should().BeTrue();
        }

        [Fact]
        public void Still_skipped_while_the_window_is_open()
        {
            var (breaker, clock) = Build();
            breaker.RecordUnavailable("t1");

            clock.Advance(SecretStoreBreaker.OpenWindow - TimeSpan.FromSeconds(1));

            breaker.ShouldSkip("t1").Should().BeTrue();
        }

        /// <summary>A run with no tenant still gets an answer rather than a null reference.</summary>
        [Fact]
        public void A_run_with_no_tenant_is_handled_like_any_other()
        {
            var (breaker, _) = Build();

            breaker.ShouldSkip(null).Should().BeFalse();
            breaker.RecordUnavailable(null);
            breaker.ShouldSkip(null).Should().BeTrue();
            breaker.ShouldSkip("t1").Should().BeFalse("and it is its own bucket");
        }

        /// <summary>
        /// The window is short on purpose: an outage that has ended should be noticed quickly, and
        /// the cost of finding out is one deferred run, never a failed one.
        /// </summary>
        [Fact]
        public void The_window_is_short()
        {
            SecretStoreBreaker.OpenWindow.Should().BeLessThanOrEqualTo(TimeSpan.FromSeconds(30));
        }
    }
}
