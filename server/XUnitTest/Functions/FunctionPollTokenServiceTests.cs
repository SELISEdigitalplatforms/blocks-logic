using Blocks.Genesis;
using FluentAssertions;
using Functions.DomainService.Entities;
using Functions.DomainService.Enums;
using Functions.DomainService.Queue;
using Functions.DomainService.Repositories;
using Functions.DomainService.Services;
using Functions.DomainService.Utils;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using StackExchange.Redis;

namespace XUnitTest.Functions
{
    /// <summary>
    /// The anonymous poll capability: what is stored (a hash, never the token), for how long, and
    /// that every way of presenting the wrong thing is the same indistinguishable "no".
    /// </summary>
    public class FunctionPollTokenServiceTests
    {
        /// <summary>An in-memory stand-in for the one Redis hash the service uses.</summary>
        private sealed class FakeRedis
        {
            public Dictionary<string, Dictionary<string, string>> Hashes { get; } = [];
            public Dictionary<string, TimeSpan?> Expiries { get; } = [];
            public Mock<IDatabase> Database { get; } = new();

            public FakeRedis()
            {
                Database.Setup(d => d.HashSetAsync(It.IsAny<RedisKey>(), It.IsAny<HashEntry[]>(), It.IsAny<CommandFlags>()))
                    .Callback<RedisKey, HashEntry[], CommandFlags>((k, entries, _) =>
                        Hashes[k.ToString()] = entries.ToDictionary(e => e.Name.ToString(), e => e.Value.ToString()))
                    .Returns(Task.CompletedTask);
                Database.Setup(d => d.KeyExpireAsync(It.IsAny<RedisKey>(), It.IsAny<TimeSpan?>(), It.IsAny<ExpireWhen>(), It.IsAny<CommandFlags>()))
                    .Callback<RedisKey, TimeSpan?, ExpireWhen, CommandFlags>((k, ttl, _, _) => Expiries[k.ToString()] = ttl)
                    .ReturnsAsync(true);
                Database.Setup(d => d.HashGetAsync(It.IsAny<RedisKey>(), It.IsAny<RedisValue[]>(), It.IsAny<CommandFlags>()))
                    .ReturnsAsync((RedisKey k, RedisValue[] fields, CommandFlags _) =>
                        fields.Select(f => Hashes.TryGetValue(k.ToString(), out var h) && h.TryGetValue(f.ToString(), out var v)
                            ? (RedisValue)v : RedisValue.Null).ToArray());
            }

            public FunctionPollTokenService Service()
            {
                var cache = new Mock<ICacheClient>();
                cache.Setup(c => c.CacheDatabase()).Returns(Database.Object);
                return new FunctionPollTokenService(cache.Object, NullLogger<FunctionPollTokenService>.Instance);
            }
        }

        [Fact]
        public async Task An_issued_token_verifies_for_its_run_and_tenant()
        {
            var redis = new FakeRedis();
            var service = redis.Service();

            var token = await service.IssueAsync("tenant_1", "run_1");

            (await service.VerifyAsync("tenant_1", "run_1", token)).Should().BeTrue();
        }

        [Fact]
        public async Task Only_the_hash_is_stored_and_it_expires_with_the_run()
        {
            var redis = new FakeRedis();
            var token = await redis.Service().IssueAsync("tenant_1", "run_1");

            var stored = redis.Hashes[FunctionQueueKeys.PollToken("run_1")];
            stored.Values.Should().NotContain(token);
            stored["hash"].Should().Be(FunctionPollTokenService.Hash(token));
            redis.Expiries[FunctionQueueKeys.PollToken("run_1")].Should().Be(FunctionQueueKeys.RunTtl);
        }

        [Fact]
        public async Task Tokens_are_32_random_bytes_and_never_repeat()
        {
            var service = new FakeRedis().Service();
            var tokens = new HashSet<string>();
            for (var i = 0; i < 50; i++) tokens.Add(await service.IssueAsync("tenant_1", $"run_{i}"));

            tokens.Should().HaveCount(50);
            tokens.Should().OnlyContain(t => t.Length == 43 && !t.Contains('=') && !t.Contains('+') && !t.Contains('/'));
        }

        [Fact]
        public async Task A_wrong_token_a_wrong_tenant_or_another_runs_token_is_refused()
        {
            var redis = new FakeRedis();
            var service = redis.Service();
            var mine = await service.IssueAsync("tenant_1", "run_1");
            var other = await service.IssueAsync("tenant_1", "run_2");

            (await service.VerifyAsync("tenant_1", "run_1", mine + "x")).Should().BeFalse();
            (await service.VerifyAsync("tenant_1", "run_1", mine.ToUpperInvariant())).Should().BeFalse();
            (await service.VerifyAsync("tenant_2", "run_1", mine)).Should().BeFalse("the token is bound to the tenant that invoked");
            (await service.VerifyAsync("tenant_1", "run_1", other)).Should().BeFalse("a token is for one run only");
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        public async Task A_missing_token_is_refused_without_a_lookup(string? token)
        {
            var redis = new FakeRedis();
            (await redis.Service().VerifyAsync("tenant_1", "run_1", token)).Should().BeFalse();
            redis.Database.Verify(d => d.HashGetAsync(It.IsAny<RedisKey>(), It.IsAny<RedisValue[]>(), It.IsAny<CommandFlags>()), Times.Never);
        }

        [Fact]
        public async Task An_oversized_token_is_refused_without_hashing_or_a_lookup()
        {
            var redis = new FakeRedis();
            (await redis.Service().VerifyAsync("tenant_1", "run_1", new string('a', 10_000))).Should().BeFalse();
            redis.Database.Verify(d => d.HashGetAsync(It.IsAny<RedisKey>(), It.IsAny<RedisValue[]>(), It.IsAny<CommandFlags>()), Times.Never);
        }

        [Fact]
        public async Task An_unknown_or_expired_run_is_the_same_refusal()
        {
            (await new FakeRedis().Service().VerifyAsync("tenant_1", "run_gone", "anything")).Should().BeFalse();
        }

        [Fact]
        public async Task Issuing_requires_a_tenant_and_a_run()
        {
            var service = new FakeRedis().Service();
            await ((Func<Task>)(() => service.IssueAsync("", "run_1"))).Should().ThrowAsync<ArgumentException>();
            await ((Func<Task>)(() => service.IssueAsync("tenant_1", " "))).Should().ThrowAsync<ArgumentException>();
        }

        // --------------------------------------------- what the poll may reveal ----

        private static IFunctionRunService RunService(FunctionRunEntity? run)
        {
            var repository = new Mock<IFunctionRunRepository>();
            repository.Setup(r => r.GetByIdAsync("tenant_1", "run_1", It.IsAny<CancellationToken>())).ReturnsAsync(run);
            return new FunctionRunService(
                repository.Object,
                new Mock<IFunctionRunLogRepository>().Object,
                new Mock<IFunctionAuditService>().Object,
                new Mock<IFunctionInvocationService>().Object,
                new Mock<ICacheClient>().Object,
                new Mock<ILogger<FunctionRunService>>().Object);
        }

        [Fact]
        public async Task The_poll_result_carries_status_and_result_and_nothing_else()
        {
            var run = new FunctionRunEntity
            {
                ItemId = "run_1",
                Status = RunStatus.Succeeded,
                Input = "{\"password\":\"p\"}",
                Result = "{\"ok\":true}",
                InvokedById = "user_9",
            };

            var dto = await RunService(run).GetPollResultAsync("tenant_1", "run_1");

            dto.Status.Should().Be(FunctionQueueKeys.Wire.Succeeded);
            dto.Result.Should().Be("{\"ok\":true}");
            System.Text.Json.JsonSerializer.Serialize(dto).Should().NotContain("password").And.NotContain("user_9");
        }

        [Fact]
        public async Task A_run_still_going_reports_its_status_but_no_result_yet()
        {
            var run = new FunctionRunEntity { ItemId = "run_1", Status = RunStatus.Running, Result = "partial" };

            var dto = await RunService(run).GetPollResultAsync("tenant_1", "run_1");

            dto.Status.Should().Be(FunctionQueueKeys.Wire.Running);
            dto.Result.Should().BeNull();
        }

        [Fact]
        public async Task A_failed_run_reports_its_error()
        {
            var run = new FunctionRunEntity
            {
                ItemId = "run_1", Status = RunStatus.Failed, ErrorCode = RunErrorCode.UserRuntimeError, ErrorMessage = "boom",
            };

            var dto = await RunService(run).GetPollResultAsync("tenant_1", "run_1");

            dto.ErrorCode.Should().Be(nameof(RunErrorCode.UserRuntimeError));
            dto.ErrorMessage.Should().Be("boom");
        }

        [Fact]
        public async Task A_missing_run_is_not_found()
        {
            var act = () => RunService(null).GetPollResultAsync("tenant_1", "run_1");
            await act.Should().ThrowAsync<FunctionNotFoundException>();
        }
    }
}
