using FluentAssertions;
using Functions.DomainService.Queue;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using StackExchange.Redis;

namespace XUnitTest.Functions
{
    /// <summary>
    /// Acknowledging is what bounds the streams, so it is worth pinning rather than trusting.
    /// Reading an entry never removes it and an acknowledged entry can never be reclaimed again,
    /// so ack without delete is what let <c>functions:results</c> grow forever.
    /// </summary>
    public class FunctionResultGroupConsumerTests
    {
        private readonly Mock<IDatabase> _database = new(MockBehavior.Loose);

        private FunctionResultGroupConsumer Consumer() =>
            new(_database.Object, NullLogger.Instance, FunctionQueueKeys.ResultsStream, "worker-1");

        [Fact]
        public async Task Acknowledging_also_deletes_the_entry()
        {
            await Consumer().AcknowledgeAsync("1700000000000-0");

            _database.Verify(
                d => d.StreamAcknowledgeAsync(
                    FunctionQueueKeys.ResultsStream, FunctionQueueKeys.LogicWorkerGroup,
                    (RedisValue)"1700000000000-0", CommandFlags.None),
                Times.Once);

            _database.Verify(
                d => d.StreamDeleteAsync(
                    FunctionQueueKeys.ResultsStream,
                    It.Is<RedisValue[]>(ids => ids.Length == 1 && ids[0] == "1700000000000-0"),
                    CommandFlags.None),
                Times.Once);
        }

        [Fact]
        public async Task The_delete_happens_after_the_acknowledge_never_before()
        {
            // Deleting first would drop the entry while it is still pending, so a crash in
            // between would leave a pending id pointing at nothing.
            var order = new List<string>();
            _database
                .Setup(d => d.StreamAcknowledgeAsync(It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<RedisValue>(), It.IsAny<CommandFlags>()))
                .Returns(() => { order.Add("ack"); return Task.FromResult(1L); });
            _database
                .Setup(d => d.StreamDeleteAsync(It.IsAny<RedisKey>(), It.IsAny<RedisValue[]>(), It.IsAny<CommandFlags>()))
                .Returns(() => { order.Add("del"); return Task.FromResult(1L); });

            await Consumer().AcknowledgeAsync("1-0");

            order.Should().ContainInOrder("ack", "del");
        }

        [Fact]
        public async Task A_group_that_went_away_is_created_again_by_the_next_read()
        {
            // Redis lost the stream (restart without the data, failover, delete) while the Worker ran: before this,
            // every read failed with NOGROUP and no result was saved until the Worker was restarted.
            var missing = new RedisServerException("NOGROUP No such key 'functions:results' or consumer group 'logic-workers'");
            _database
                .Setup(d => d.StreamReadGroupAsync(It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<RedisValue>(),
                    It.IsAny<RedisValue?>(), It.IsAny<int?>(), It.IsAny<bool>(), It.IsAny<TimeSpan?>(), It.IsAny<CommandFlags>()))
                .ThrowsAsync(missing);

            var read = await Consumer().ReadNewAsync(10);

            read.Should().BeEmpty();
            _database.Verify(
                d => d.StreamCreateConsumerGroupAsync(
                    FunctionQueueKeys.ResultsStream, FunctionQueueKeys.LogicWorkerGroup, StreamPosition.Beginning, true, CommandFlags.None),
                Times.Once);
        }
    }
}
