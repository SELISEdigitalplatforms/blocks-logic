using Blocks.Genesis;
using FluentAssertions;
using Functions.DomainService.Queue;
using Functions.DomainService.Services;
using Moq;
using StackExchange.Redis;
using XUnitTest.TestHelpers;

namespace XUnitTest.Functions
{
    /// <summary>
    /// Revoking a finished run's grant: delete the grant named on the run hash, then clear the field. Never
    /// throws (the grant's TTL is the backstop) and never logs the grant id.
    /// </summary>
    public class FunctionRunGrantRevokerTests
    {
        private const string RunId = "run-1";
        private const string Grant = "dg_00112233445566778899aabbccddeeff00112233445566778899aabbccddeeff";

        private readonly (IDatabase Database, FakeRedisDatabase Fake) _redis = FakeRedisDatabase.Create();
        private readonly Mock<IFunctionDelegationService> _delegation = new();
        private readonly CapturingLogger<FunctionRunGrantRevoker> _log = new();

        private FunctionRunGrantRevoker Revoker()
        {
            var cache = new Mock<ICacheClient>();
            cache.Setup(c => c.CacheDatabase()).Returns(_redis.Database);
            return new FunctionRunGrantRevoker(cache.Object, _delegation.Object, _log);
        }

        [Fact]
        public async Task The_grant_is_deleted_and_the_field_cleared()
        {
            _redis.Fake.On("HashGetAsync", _ => (RedisValue)Grant);

            await Revoker().RevokeAsync(RunId);

            _redis.Fake.Calls("HashGetAsync").Should().ContainSingle(a =>
                a[0]!.ToString() == FunctionQueueKeys.Run(RunId) && a[1]!.ToString() == FunctionQueueKeys.RunDelegationField);
            _delegation.Verify(d => d.DeleteGrantAsync(Grant), Times.Once);
            _redis.Fake.Calls("HashDeleteAsync").Should().ContainSingle(a =>
                a[0]!.ToString() == FunctionQueueKeys.Run(RunId) && a[1]!.ToString() == FunctionQueueKeys.RunDelegationField);
            var names = _redis.Fake.CallNames.ToList();
            names.IndexOf("HashGetAsync").Should().BeLessThan(names.IndexOf("HashDeleteAsync"));
        }

        [Fact]
        public async Task A_run_without_a_grant_is_left_alone()
        {
            _redis.Fake.On("HashGetAsync", _ => RedisValue.Null);

            await Revoker().RevokeAsync(RunId);

            _delegation.Verify(d => d.DeleteGrantAsync(It.IsAny<string?>()), Times.Never);
            _redis.Fake.Calls("HashDeleteAsync").Should().BeEmpty();
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("  ")]
        public async Task No_run_id_is_nothing_to_do(string? runId)
        {
            await Revoker().RevokeAsync(runId);

            _redis.Fake.CallNames.Should().BeEmpty();
        }

        [Fact]
        public async Task Redis_down_never_throws_and_never_logs_the_grant()
        {
            _redis.Fake.On("HashGetAsync", _ => throw new RedisConnectionException(ConnectionFailureType.SocketFailure, "down"));

            var act = () => Revoker().RevokeAsync(RunId);

            await act.Should().NotThrowAsync();
            _log.All.Should().Contain(RunId).And.Contain(nameof(RedisConnectionException)).And.NotContain(Grant);
        }

        [Fact]
        public async Task A_failing_delete_never_throws_and_never_logs_the_grant()
        {
            _redis.Fake.On("HashGetAsync", _ => (RedisValue)Grant);
            _delegation.Setup(d => d.DeleteGrantAsync(Grant)).ThrowsAsync(new InvalidOperationException("boom " + Grant));

            var act = () => Revoker().RevokeAsync(RunId);

            await act.Should().NotThrowAsync();
            _log.All.Should().NotContain(Grant);
        }
    }
}
