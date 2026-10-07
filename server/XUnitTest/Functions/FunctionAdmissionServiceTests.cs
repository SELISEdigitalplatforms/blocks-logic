using Blocks.Genesis;
using FluentAssertions;
using Functions.DomainService.Entities;
using Functions.DomainService.Enums;
using Functions.DomainService.Models;
using Functions.DomainService.Services;
using Functions.DomainService.Utils;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using StackExchange.Redis;

namespace XUnitTest.Functions
{
    /// <summary>
    /// The product decision is that nothing is refused for volume in V1. That is easy to
    /// state and easy to break by accident, so most of these tests assert a <i>negative</i>:
    /// that no counter was touched and no request refused. The 429 cases exist to prove the
    /// mechanism still works when someone deliberately switches it on.
    /// </summary>
    public class FunctionAdmissionServiceTests
    {
        private static FunctionEntity Function(int? perMinute = null, int? perDay = null) => new()
        {
            ItemId = "fn_1",
            Limits = new FunctionLimits { RequestsPerMinute = perMinute, RequestsPerDay = perDay },
        };

        private static IConfiguration Config(bool rateLimitsEnabled) =>
            new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Functions:RateLimits:Enabled"] = rateLimitsEnabled ? "true" : "false",
                })
                .Build();

        /// <summary>A cache whose counter returns <paramref name="count"/> and records every call.</summary>
        private static (Mock<ICacheClient> Cache, Mock<IDatabase> Database) Cache(long count = 1)
        {
            var database = new Mock<IDatabase>(MockBehavior.Loose);
            database
                .Setup(d => d.StringIncrementAsync(It.IsAny<RedisKey>(), It.IsAny<long>(), It.IsAny<CommandFlags>()))
                .ReturnsAsync(count);
            database
                .Setup(d => d.KeyExpireAsync(It.IsAny<RedisKey>(), It.IsAny<TimeSpan?>(), It.IsAny<CommandFlags>()))
                .ReturnsAsync(true);

            var cache = new Mock<ICacheClient>();
            cache.Setup(c => c.CacheDatabase()).Returns(database.Object);
            return (cache, database);
        }

        private static FunctionAdmissionService Service(Mock<ICacheClient> cache, bool rateLimitsEnabled) =>
            new(cache.Object, Config(rateLimitsEnabled), NullLogger<FunctionAdmissionService>.Instance);

        [Fact]
        public async Task Admits_a_normal_request()
        {
            var (cache, _) = Cache();
            var result = await Service(cache, false).AdmitAsync(Function(), "tenant_1", "{}");

            result.IsAdmitted.Should().BeTrue();
        }

        [Fact]
        public async Task Touches_no_counter_at_all_when_rate_limiting_is_disabled()
        {
            // The default path must cost nothing: no INCR, no EXPIRE, no Redis round trip.
            var (cache, database) = Cache(count: 999_999);

            var result = await Service(cache, rateLimitsEnabled: false)
                .AdmitAsync(Function(perMinute: 1, perDay: 1), "tenant_1", "{}");

            result.IsAdmitted.Should().BeTrue("rate limiting is off, so the configured values are ignored");
            database.Verify(
                d => d.StringIncrementAsync(It.IsAny<RedisKey>(), It.IsAny<long>(), It.IsAny<CommandFlags>()),
                Times.Never);
        }

        [Fact]
        public async Task Touches_no_counter_when_enabled_but_the_function_sets_no_limit()
        {
            // Both the switch AND a per-function value are required.
            var (cache, database) = Cache(count: 999_999);

            var result = await Service(cache, rateLimitsEnabled: true)
                .AdmitAsync(Function(), "tenant_1", "{}");

            result.IsAdmitted.Should().BeTrue();
            database.Verify(
                d => d.StringIncrementAsync(It.IsAny<RedisKey>(), It.IsAny<long>(), It.IsAny<CommandFlags>()),
                Times.Never);
        }

        private static FunctionAdmissionService Service(Mock<ICacheClient> cache, DateTime now) =>
            new(cache.Object, Config(true), NullLogger<FunctionAdmissionService>.Instance, () => now);

        [Fact]
        public async Task Refuses_only_when_enabled_and_the_per_minute_limit_is_exceeded()
        {
            // Thrown, not returned: the invocation service would otherwise rewrap the verdict
            // as a 400 validation failure, and a client does not retry a 400.
            var (cache, _) = Cache(count: 11);
            var now = new DateTime(2026, 9, 29, 10, 15, 42, DateTimeKind.Utc);

            var act = () => Service(cache, now).AdmitAsync(Function(perMinute: 10), "tenant_1", "{}");

            var refusal = (await act.Should().ThrowAsync<FunctionRateLimitedException>()).Which;
            refusal.Message.Should().Contain("10 requests per minute");
            refusal.RetryAfterSeconds.Should().Be(18, "the minute window rolls over at 10:16:00");
        }

        [Fact]
        public async Task A_rate_limit_refusal_is_not_a_validation_failure()
        {
            // The two must stay distinct types: the controller maps one to 400 and the other
            // to 429, and a subclass relationship would let the 400 catch win.
            typeof(FunctionValidationException).IsAssignableFrom(typeof(FunctionRateLimitedException)).Should().BeFalse();

            var (cache, _) = Cache(count: 2);
            var act = () => Service(cache, rateLimitsEnabled: true).AdmitAsync(Function(perMinute: 1), "tenant_1", "{}");
            await act.Should().ThrowAsync<FunctionRateLimitedException>();
        }

        [Fact]
        public async Task Retry_after_is_never_zero_even_on_the_last_tick_of_a_window()
        {
            var (cache, _) = Cache(count: 11);
            var now = new DateTime(2026, 9, 29, 10, 15, 59, 999, DateTimeKind.Utc);

            var act = () => Service(cache, now).AdmitAsync(Function(perMinute: 10), "tenant_1", "{}");

            (await act.Should().ThrowAsync<FunctionRateLimitedException>()).Which.RetryAfterSeconds.Should().Be(1);
        }

        [Fact]
        public async Task The_minute_window_key_is_per_function()
        {
            var (cache, database) = Cache(count: 1);
            var now = new DateTime(2026, 9, 29, 10, 15, 42, DateTimeKind.Utc);

            await Service(cache, now).AdmitAsync(Function(perMinute: 10), "tenant_1", "{}");

            database.Verify(d => d.StringIncrementAsync(
                (RedisKey)"function:rate:fn_1:202609291015", It.IsAny<long>(), It.IsAny<CommandFlags>()), Times.Once);
        }

        [Fact]
        public async Task The_daily_quota_key_is_per_function_not_per_tenant()
        {
            // The limit is each function's own RequestsPerDay. A tenant-wide counter let one
            // busy function spend every other function's allowance.
            var (cache, database) = Cache(count: 1);
            var now = new DateTime(2026, 9, 29, 10, 15, 42, DateTimeKind.Utc);
            var other = Function(perDay: 100);
            other.ItemId = "fn_2";

            await Service(cache, now).AdmitAsync(Function(perDay: 100), "tenant_1", "{}");
            await Service(cache, now).AdmitAsync(other, "tenant_1", "{}");

            database.Verify(d => d.StringIncrementAsync(
                (RedisKey)"function:quota:tenant_1:fn_1:20260929", It.IsAny<long>(), It.IsAny<CommandFlags>()), Times.Once);
            database.Verify(d => d.StringIncrementAsync(
                (RedisKey)"function:quota:tenant_1:fn_2:20260929", It.IsAny<long>(), It.IsAny<CommandFlags>()), Times.Once);
        }

        [Fact]
        public async Task The_counter_ttl_is_set_on_first_increment_only()
        {
            var (cache, database) = Cache(count: 1);
            await Service(cache, rateLimitsEnabled: true).AdmitAsync(Function(perMinute: 5), "tenant_1", "{}");
            database.Verify(d => d.KeyExpireAsync(It.IsAny<RedisKey>(), TimeSpan.FromMinutes(2), It.IsAny<ExpireWhen>(), It.IsAny<CommandFlags>()), Times.Once);

            var (cache2, database2) = Cache(count: 3);
            await Service(cache2, rateLimitsEnabled: true).AdmitAsync(Function(perMinute: 5), "tenant_1", "{}");
            database2.Verify(d => d.KeyExpireAsync(It.IsAny<RedisKey>(), It.IsAny<TimeSpan?>(), It.IsAny<ExpireWhen>(), It.IsAny<CommandFlags>()), Times.Never);
        }

        [Fact]
        public async Task A_per_minute_refusal_does_not_also_spend_the_daily_quota()
        {
            var (cache, database) = Cache(count: 11);

            var act = () => Service(cache, rateLimitsEnabled: true)
                .AdmitAsync(Function(perMinute: 10, perDay: 1000), "tenant_1", "{}");

            await act.Should().ThrowAsync<FunctionRateLimitedException>();
            database.Verify(d => d.StringIncrementAsync(
                It.Is<RedisKey>(k => k.ToString().StartsWith("function:quota:")), It.IsAny<long>(), It.IsAny<CommandFlags>()), Times.Never);
        }

        [Fact]
        public async Task Admits_at_exactly_the_per_minute_limit()
        {
            // The limit is inclusive: the tenth request of ten is allowed.
            var (cache, _) = Cache(count: 10);

            var result = await Service(cache, rateLimitsEnabled: true)
                .AdmitAsync(Function(perMinute: 10), "tenant_1", "{}");

            result.IsAdmitted.Should().BeTrue();
        }

        [Fact]
        public async Task Refuses_when_the_daily_quota_is_exceeded_with_retry_after_until_utc_midnight()
        {
            var (cache, _) = Cache(count: 101);
            var now = new DateTime(2026, 9, 29, 23, 0, 0, DateTimeKind.Utc);

            var act = () => Service(cache, now).AdmitAsync(Function(perDay: 100), "tenant_1", "{}");

            var refusal = (await act.Should().ThrowAsync<FunctionRateLimitedException>()).Which;
            refusal.Message.Should().Contain("100 requests per day");
            refusal.RetryAfterSeconds.Should().Be(3600);
        }

        [Fact]
        public async Task Admits_at_exactly_the_daily_limit()
        {
            var (cache, _) = Cache(count: 100);

            var result = await Service(cache, rateLimitsEnabled: true).AdmitAsync(Function(perDay: 100), "tenant_1", "{}");

            result.IsAdmitted.Should().BeTrue();
        }

        [Fact]
        public void Rate_limiting_is_on_unless_switched_off()
        {
            // Decided 2026-10-07 (FN-19): on with no key at all; Enabled=false turns it off.
            var (cache, _) = Cache(count: 999_999);
            var service = new FunctionAdmissionService(
                cache.Object, new ConfigurationBuilder().Build(), NullLogger<FunctionAdmissionService>.Instance);

            var act = () => service.AdmitAsync(Function(perMinute: 1), "tenant_1", "{}");
            act.Should().ThrowAsync<FunctionRateLimitedException>().GetAwaiter().GetResult();
        }

        [Fact]
        public async Task An_oversized_input_is_refused_even_with_rate_limiting_off()
        {
            // Not a volume refusal: a payload this size cannot be delivered to a sandbox at
            // all, so accepting it would create a run that could only fail.
            var (cache, _) = Cache();
            var big = new string('x', (int)FunctionLimits.Ceiling.InputBytes + 1024);

            var result = await Service(cache, rateLimitsEnabled: false)
                .AdmitAsync(Function(), "tenant_1", big);

            result.Outcome.Should().Be(AdmissionOutcome.InputTooLarge);
            result.Message.Should().Contain("over the");
        }

        [Fact]
        public async Task Input_exactly_at_the_ceiling_is_admitted()
        {
            var (cache, _) = Cache();
            var exact = new string('x', (int)FunctionLimits.Ceiling.InputBytes);

            var result = await Service(cache, false).AdmitAsync(Function(), "tenant_1", exact);

            result.IsAdmitted.Should().BeTrue();
        }

        [Fact]
        public async Task A_null_input_is_fine()
        {
            var (cache, _) = Cache();
            (await Service(cache, false).AdmitAsync(Function(), "tenant_1", null))
                .IsAdmitted.Should().BeTrue();
        }

        [Fact]
        public async Task An_unavailable_redis_admits_rather_than_refuses()
        {
            // Failing closed here would refuse real traffic because a counter could not be
            // written — for a limiter that is switched off by default, that is the wrong trade.
            var database = new Mock<IDatabase>();
            database
                .Setup(d => d.StringIncrementAsync(It.IsAny<RedisKey>(), It.IsAny<long>(), It.IsAny<CommandFlags>()))
                .ThrowsAsync(new RedisTimeoutException("down", CommandStatus.Unknown));
            var cache = new Mock<ICacheClient>();
            cache.Setup(c => c.CacheDatabase()).Returns(database.Object);

            var result = await Service(cache, rateLimitsEnabled: true)
                .AdmitAsync(Function(perMinute: 1), "tenant_1", "{}");

            result.IsAdmitted.Should().BeTrue();
        }

        [Fact]
        public async Task Concurrency_is_never_an_admission_decision()
        {
            // Concurrency is a scheduling limit: the run is created and enqueued regardless,
            // and the runner's semaphore decides when it starts. Overflow queues, never
            // rejects — checking it here would turn a queue into an error.
            var (cache, database) = Cache(count: 999_999);
            var function = Function();
            function.Limits.Concurrency = 1;

            var result = await Service(cache, rateLimitsEnabled: true)
                .AdmitAsync(function, "tenant_1", "{}");

            result.IsAdmitted.Should().BeTrue();
            database.Verify(
                d => d.StringIncrementAsync(It.IsAny<RedisKey>(), It.IsAny<long>(), It.IsAny<CommandFlags>()),
                Times.Never);
        }

        // ---- FN-19: a default limit for public functions (2026-10-07) ----------------------------

        private static FunctionEntity Public(int? perMinute = null) => new()
        {
            ItemId = "fn_pub",
            Limits = new FunctionLimits { RequestsPerMinute = perMinute },
            Trigger = new TriggerConfig { AuthMode = AuthMode.Public },
        };

        private static FunctionAdmissionService DefaultService(Mock<ICacheClient> cache, params (string Key, string Value)[] settings) =>
            new(cache.Object,
                new ConfigurationBuilder().AddInMemoryCollection(settings.ToDictionary(s => s.Key, s => (string?)s.Value)).Build(),
                NullLogger<FunctionAdmissionService>.Instance);

        [Fact]
        public async Task A_public_function_gets_600_a_minute_with_no_configuration_at_all()
        {
            var (cache, _) = Cache(count: 601);

            var act = () => DefaultService(cache).AdmitAsync(Public(), "tenant_1", "{}", InvokedByType.Http, null);

            (await act.Should().ThrowAsync<FunctionRateLimitedException>()).Which.Message.Should().Contain("600 requests per minute");
        }

        [Fact]
        public async Task The_600th_call_of_a_minute_still_passes()
        {
            var (cache, _) = Cache(count: 600);

            (await DefaultService(cache).AdmitAsync(Public(), "tenant_1", "{}", InvokedByType.Http, null)).IsAdmitted.Should().BeTrue();
        }

        [Fact]
        public async Task A_private_function_has_no_limit_unless_its_tenant_sets_one()
        {
            var (cache, database) = Cache(count: 999_999);
            var token = Function();   // Token is the default access

            (await DefaultService(cache).AdmitAsync(token, "tenant_1", "{}", InvokedByType.Http, null)).IsAdmitted.Should().BeTrue();
            database.Verify(d => d.StringIncrementAsync(It.IsAny<RedisKey>(), It.IsAny<long>(), It.IsAny<CommandFlags>()), Times.Never);

            var act = () => DefaultService(cache).AdmitAsync(Function(perMinute: 50), "tenant_1", "{}", InvokedByType.Http, null);
            await act.Should().ThrowAsync<FunctionRateLimitedException>();
        }

        [Fact]
        public async Task The_tenants_own_number_wins_over_the_public_default()
        {
            var (cache, _) = Cache(count: 900);

            (await DefaultService(cache).AdmitAsync(Public(perMinute: 1000), "tenant_1", "{}", InvokedByType.Http, null))
                .IsAdmitted.Should().BeTrue("1000 set by the tenant, 900 used");
        }

        [Theory]
        [InlineData(InvokedByType.Workflow)]
        [InlineData(InvokedByType.Test)]
        public async Task Workflow_and_test_calls_are_never_counted(InvokedByType invokedBy)
        {
            var (cache, database) = Cache(count: 999_999);

            (await DefaultService(cache).AdmitAsync(Public(perMinute: 1), "tenant_1", "{}", invokedBy, null)).IsAdmitted.Should().BeTrue();
            database.Verify(d => d.StringIncrementAsync(It.IsAny<RedisKey>(), It.IsAny<long>(), It.IsAny<CommandFlags>()), Times.Never);
        }

        [Fact]
        public async Task The_deployed_versions_access_and_limit_are_the_live_ones()
        {
            var (cache, _) = Cache(count: 601);
            // Draft says Token; the live version is Public with no limit of its own → the default applies.
            var version = new FunctionVersionEntity { Trigger = new TriggerConfig { AuthMode = AuthMode.Public }, Limits = new FunctionLimits() };

            var act = () => DefaultService(cache).AdmitAsync(Function(), "tenant_1", "{}", InvokedByType.Http, version);

            await act.Should().ThrowAsync<FunctionRateLimitedException>();
        }

        [Fact]
        public async Task The_switch_and_the_default_are_configurable()
        {
            var (cache, _) = Cache(count: 999_999);

            (await DefaultService(cache, ("Functions:RateLimits:Enabled", "false"))
                .AdmitAsync(Public(), "tenant_1", "{}", InvokedByType.Http, null)).IsAdmitted.Should().BeTrue("everything off");
            (await DefaultService(cache, ("Functions:RateLimits:PublicPerMinute", "0"))
                .AdmitAsync(Public(), "tenant_1", "{}", InvokedByType.Http, null)).IsAdmitted.Should().BeTrue("no public default");
        }

        [Theory]
        [InlineData(0, null)]
        [InlineData(-5, null)]
        [InlineData(1, 1)]
        [InlineData(100_000, 100_000)]
        [InlineData(100_001, null)]
        public void A_saved_limit_is_kept_only_when_sane(int value, int? kept)
        {
            new FunctionLimits { RequestsPerMinute = value, RequestsPerDay = 5 }.Clamp().RequestsPerMinute.Should().Be(kept);
            new FunctionLimits { RequestsPerDay = 5 }.Clamp().RequestsPerDay.Should().BeNull("per day stays off");
        }
    }
}
