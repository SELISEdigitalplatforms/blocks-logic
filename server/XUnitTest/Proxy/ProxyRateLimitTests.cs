using Blocks.Genesis;
using Common.InternalService.Access;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Proxy.DomainService.Dtos;
using Proxy.DomainService.Entities;
using Proxy.DomainService.Repositories;
using Proxy.DomainService.Services;
using Proxy.DomainService.Utils;
using StackExchange.Redis;
using XUnitTest.TestHelpers;

namespace XUnitTest.Proxy
{
    /// <summary>
    /// The gateway rate limit (P-3): per proxy per minute, the same rule as functions (FN-19). Public proxies
    /// get 600 unless the tenant set its own; token proxies get none unless set. Redis down fails open.
    /// </summary>
    public class ProxyRateLimitTests
    {
        private sealed class FixedTime(DateTimeOffset now) : TimeProvider
        {
            public override DateTimeOffset GetUtcNow() => now;
        }

        // 12:34:45 UTC → 15 s left in the minute.
        private static readonly DateTimeOffset Now = new(2026, 10, 7, 12, 34, 45, TimeSpan.Zero);

        private static (Mock<ICacheClient> Cache, Mock<IDatabase> Database, List<string> Keys) Cache(long count)
        {
            var keys = new List<string>();
            var database = new Mock<IDatabase>(MockBehavior.Loose);
            database
                .Setup(d => d.StringIncrementAsync(It.IsAny<RedisKey>(), It.IsAny<long>(), It.IsAny<CommandFlags>()))
                .Callback<RedisKey, long, CommandFlags>((k, _, _) => keys.Add(k!))
                .ReturnsAsync(count);
            database
                .Setup(d => d.KeyExpireAsync(It.IsAny<RedisKey>(), It.IsAny<TimeSpan?>(), It.IsAny<CommandFlags>()))
                .ReturnsAsync(true);
            var cache = new Mock<ICacheClient>();
            cache.Setup(c => c.CacheDatabase()).Returns(database.Object);
            return (cache, database, keys);
        }

        private static ProxyRateLimiter Limiter(Mock<ICacheClient> cache, bool? enabled = null, int? publicPerMinute = null)
        {
            var values = new Dictionary<string, string?>();
            if (enabled is not null) values["Proxy:RateLimits:Enabled"] = enabled.Value ? "true" : "false";
            if (publicPerMinute is not null) values["Proxy:RateLimits:PublicPerMinute"] = publicPerMinute.ToString();
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
            return new ProxyRateLimiter(cache.Object, configuration, new FixedTime(Now), NullLogger<ProxyRateLimiter>.Instance);
        }

        private static ProxyResolvedConfig Config(bool isPublic, int? perMinute = null) => new()
        {
            ProxyId = "p1",
            Slug = "openai",
            Upstream = "https://api.openai.com",
            Access = isPublic ? EndpointAccessPolicy.AllowPublic() : EndpointAccessPolicy.RequireToken(),
            RequestsPerMinute = perMinute,
        };

        private static void VerifyNoCounter(Mock<IDatabase> database) =>
            database.Verify(
                d => d.StringIncrementAsync(It.IsAny<RedisKey>(), It.IsAny<long>(), It.IsAny<CommandFlags>()),
                Times.Never);

        // ---------- limiter ----------

        [Fact]
        public async Task Public_with_no_own_limit_gets_600_by_default()
        {
            var (cache, _, keys) = Cache(count: 600);
            (await Limiter(cache).TryAcquireAsync("t1", Config(isPublic: true))).Should().BeNull("600 is still inside the limit");

            var (over, _, _) = Cache(count: 601);
            (await Limiter(over).TryAcquireAsync("t1", Config(isPublic: true))).Should().Be(15);
            keys.Should().ContainSingle().Which.Should().Be("blocks-logic:proxy:rate:t1:p1:202610071234");
        }

        [Fact]
        public async Task Token_proxy_with_no_own_limit_is_not_limited_and_costs_no_redis_call()
        {
            var (cache, database, _) = Cache(count: 1_000_000);

            (await Limiter(cache).TryAcquireAsync("t1", Config(isPublic: false))).Should().BeNull();
            VerifyNoCounter(database);
        }

        [Fact]
        public async Task The_tenants_own_limit_wins_for_public_and_token_proxies()
        {
            var (cache, _, _) = Cache(count: 6);

            (await Limiter(cache).TryAcquireAsync("t1", Config(isPublic: true, perMinute: 5))).Should().Be(15);
            (await Limiter(cache).TryAcquireAsync("t1", Config(isPublic: false, perMinute: 5))).Should().Be(15);
            (await Limiter(cache).TryAcquireAsync("t1", Config(isPublic: true, perMinute: 1000))).Should().BeNull();
        }

        [Fact]
        public async Task Switched_off_counts_nothing()
        {
            var (cache, database, _) = Cache(count: 1_000_000);

            (await Limiter(cache, enabled: false).TryAcquireAsync("t1", Config(isPublic: true, perMinute: 1))).Should().BeNull();
            VerifyNoCounter(database);
        }

        [Fact]
        public async Task Public_default_zero_means_no_default()
        {
            var (cache, database, _) = Cache(count: 1_000_000);

            (await Limiter(cache, publicPerMinute: 0).TryAcquireAsync("t1", Config(isPublic: true))).Should().BeNull();
            VerifyNoCounter(database);
        }

        [Fact]
        public async Task Redis_down_lets_the_call_through()
        {
            var database = new Mock<IDatabase>();
            database
                .Setup(d => d.StringIncrementAsync(It.IsAny<RedisKey>(), It.IsAny<long>(), It.IsAny<CommandFlags>()))
                .ThrowsAsync(new RedisConnectionException(ConnectionFailureType.UnableToConnect, "down"));
            var cache = new Mock<ICacheClient>();
            cache.Setup(c => c.CacheDatabase()).Returns(database.Object);

            (await Limiter(cache).TryAcquireAsync("t1", Config(isPublic: true, perMinute: 1))).Should().BeNull();
        }

        [Fact]
        public async Task Window_ttl_is_set_once_on_the_first_call_only()
        {
            var (first, firstDb, _) = Cache(count: 1);
            await Limiter(first).TryAcquireAsync("t1", Config(isPublic: true));
            firstDb.Verify(d => d.KeyExpireAsync(It.IsAny<RedisKey>(), TimeSpan.FromMinutes(2), It.IsAny<ExpireWhen>(), It.IsAny<CommandFlags>()), Times.Once);

            var (later, laterDb, _) = Cache(count: 2);
            await Limiter(later).TryAcquireAsync("t1", Config(isPublic: true));
            laterDb.Verify(d => d.KeyExpireAsync(It.IsAny<RedisKey>(), It.IsAny<TimeSpan?>(), It.IsAny<ExpireWhen>(), It.IsAny<CommandFlags>()), Times.Never);
        }

        // ---------- validation ----------

        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        [InlineData(100_001)]
        public void Validator_refuses_a_limit_outside_1_to_100000(int perMinute)
        {
            var result = Validate(perMinute);

            result.IsValid.Should().BeFalse();
            result.Errors.Should().ContainKey("requestsPerMinute");
        }

        [Theory]
        [InlineData(null)]
        [InlineData(1)]
        [InlineData(100_000)]
        public void Validator_accepts_null_and_the_range_ends(int? perMinute)
        {
            var result = Validate(perMinute);

            result.IsValid.Should().BeTrue();
            result.RequestsPerMinute.Should().Be(perMinute);
        }

        private static ProxyConfigValidationResult Validate(int? perMinute) =>
            ProxyConfigValidator.Validate(
                "OpenAI", "https://api.openai.com/v1/chat", new[] { "POST" }, null, null,
                requestsPerMinute: perMinute);

        // ---------- history and revert ----------

        [Fact]
        public void Change_set_records_the_limit_and_revert_restores_it()
        {
            var before = new ProxyConfigSnapshot { RequestsPerMinute = null };
            var after = new ProxyConfigSnapshot { RequestsPerMinute = 50 };

            var change = ProxyChangeSet.Diff(before, after).Should().ContainSingle().Subject;
            change.Field.Should().Be("rateLimit");
            change.Before.Should().BeNull();
            change.After.Should().Be("50");

            var proxy = new ProxyDetailEntity
            {
                TenantId = "t1", Name = "n", Slug = "s", Upstream = "https://x.test", RequestsPerMinute = 50,
            };
            ProxyChangeSet.ReadField(proxy, "rateLimit").Should().Be("50");
            ProxyChangeSet.ApplyField(proxy, "rateLimit", change.Before);
            proxy.RequestsPerMinute.Should().BeNull();
            ProxyChangeSet.ApplyField(proxy, "rateLimit", "0");
            proxy.RequestsPerMinute.Should().BeNull("a stored value the validator would refuse falls back to the default");
        }

        [Fact]
        public async Task Update_that_changes_only_the_limit_saves_it_and_writes_a_version()
        {
            var (service, entity, versions) = UpdateFixture();

            var result = await service.UpdateAsync("t1", UpdateRequest(perMinute: 120));

            result.IsSuccess.Should().BeTrue();
            entity.RequestsPerMinute.Should().Be(120);
            versions.Should().ContainSingle().Which.Changes.Should().ContainSingle(c => c.Field == "rateLimit");
        }

        [Fact]
        public async Task Update_that_changes_only_resilience_is_saved_not_dropped_as_a_no_op()
        {
            // Before this fix the update's candidate snapshot left Resilience out, so a resilience-only
            // edit diffed as "no change" and was never saved.
            var (service, entity, versions) = UpdateFixture();
            var request = UpdateRequest(perMinute: null);
            request.Resilience = new ProxyResilienceInputDto { TimeoutSeconds = 10 };

            var result = await service.UpdateAsync("t1", request);

            result.IsSuccess.Should().BeTrue();
            entity.Resilience!.TimeoutSeconds.Should().Be(10);
            versions.Should().ContainSingle();
        }

        private static (ProxyService Service, ProxyDetailEntity Entity, List<ProxyVersionEntity> Versions) UpdateFixture()
        {
            TestBlocksContext.Set("t1", "user-1");
            var entity = new ProxyDetailEntity
            {
                ItemId = "p1",
                TenantId = "t1",
                Name = "OpenAI",
                Slug = "openai",
                Upstream = "https://api.openai.com/v1/chat",
                Methods = new List<HttpMethodType> { HttpMethodType.Post },
                CurrentVersion = 1,
            };
            var proxies = new Mock<IProxyRepository>();
            proxies.Setup(r => r.SaveConfigAsync(It.IsAny<ProxyDetailEntity>(), It.IsAny<int>())).ReturnsAsync(true);
            proxies.Setup(r => r.GetAsync("t1", "p1")).ReturnsAsync(entity);
            var versions = new List<ProxyVersionEntity>();
            var versionRepo = new Mock<IProxyVersionRepository>();
            versionRepo.Setup(r => r.InsertAsync(It.IsAny<ProxyVersionEntity>()))
                .Callback<ProxyVersionEntity>(versions.Add).Returns(Task.CompletedTask);
            var service = new ProxyService(
                proxies.Object, versionRepo.Object, Mock.Of<IProxyExecutionRepository>(),
                TimeProvider.System, Mock.Of<ILogger<ProxyService>>());
            return (service, entity, versions);
        }

        private static ProxyUpdateRequestDto UpdateRequest(int? perMinute) => new()
        {
            ItemId = "p1",
            Name = "OpenAI",
            Upstream = "https://api.openai.com/v1/chat",
            Methods = new List<string> { "POST" },
            RequestsPerMinute = perMinute,
        };
    }
}
