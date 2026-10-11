using Blocks.FunctionRunner.Contracts;
using Blocks.FunctionRunner.Options;
using FluentAssertions;
using Xunit;

namespace Blocks.FunctionRunner.Tests
{
    /// <summary>
    /// What a test run is allowed to take from the fleet, and from the function it is testing.
    /// <para>
    /// The rule being pinned is "a tenant exercising the editor cannot degrade what their deployed
    /// functions are serving, and cannot crowd out another tenant".
    /// </para>
    /// </summary>
    [Collection("redis-key-prefix")]
    public class TestRunIsolationTests
    {
        [Fact]
        public void A_test_draws_on_a_different_budget_from_the_deployed_function()
        {
            // Same function id, different key: a test and a live invocation must not compete.
            RedisKeys.TestConcurrency("fn-1").Should().NotBe(RedisKeys.Concurrency("fn-1"));
        }

        [Fact]
        public void A_tenant_s_slots_are_counted_per_tenant_and_nothing_else()
        {
            RedisKeys.TenantSlots("t-1").Should().NotBe(RedisKeys.TenantSlots("t-2"));
            RedisKeys.TenantSlots("t-1").Should().NotBe(RedisKeys.Concurrency("t-1"));
        }

        [Fact]
        public void Every_new_key_is_namespaced_with_the_rest()
        {
            var original = RedisKeys.Prefix;
            try
            {
                RedisKeys.Prefix = "local";

                RedisKeys.TenantSlots("t-1").Should().StartWith("local:");
                RedisKeys.TestConcurrency("fn-1").Should().StartWith("local:");
            }
            finally
            {
                RedisKeys.Prefix = original;
            }
        }

        [Theory]
        // Half the host's capacity, rounded up, so one tenant can never hold a runner outright
        // while still letting a single-slot host work at all.
        [InlineData(10, 5)]
        [InlineData(1, 1)]
        [InlineData(3, 2)]
        public void The_default_tenant_share_is_half_the_host(int capacity, int expected)
        {
            new RunnerOptions().TenantSlotLimit(capacity).Should().Be(expected);
        }

        [Fact]
        public void An_explicit_share_overrides_the_derived_one()
        {
            new RunnerOptions { MaxSandboxesPerTenant = 3 }.TenantSlotLimit(10).Should().Be(3);

            // At or above capacity the gate stops binding, which is how an operator turns the
            // share off without a separate flag.
            new RunnerOptions { MaxSandboxesPerTenant = 10 }.TenantSlotLimit(10).Should().Be(10);
        }

        [Fact]
        public void A_job_is_not_a_test_unless_the_test_loop_says_so()
        {
            // IsTest is never read off the wire: the control plane does not get to choose which
            // budget a runner draws on.
            var job = new RunJob { RunId = "r1", FunctionId = "fn-1", Image = "img" };

            job.IsTest.Should().BeFalse();
            (job with { IsTest = true }).IsTest.Should().BeTrue();
        }
    }
}
