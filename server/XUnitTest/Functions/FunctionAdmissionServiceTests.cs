using Blocks.Genesis;
using FluentAssertions;
using Functions.DomainService.Entities;
using Functions.DomainService.Models;
using Functions.DomainService.Services;
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

        [Fact]
        public async Task Refuses_only_when_enabled_and_the_per_minute_limit_is_exceeded()
        {
            var (cache, _) = Cache(count: 11);

            var result = await Service(cache, rateLimitsEnabled: true)
                .AdmitAsync(Function(perMinute: 10), "tenant_1", "{}");

            result.Outcome.Should().Be(AdmissionOutcome.RateLimited);
            result.RetryAfterSeconds.Should().NotBeNull();
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
        public async Task Refuses_when_the_daily_quota_is_exceeded()
        {
            var (cache, _) = Cache(count: 101);

            var result = await Service(cache, rateLimitsEnabled: true)
                .AdmitAsync(Function(perDay: 100), "tenant_1", "{}");

            result.Outcome.Should().Be(AdmissionOutcome.QuotaExceeded);
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
    }
}
