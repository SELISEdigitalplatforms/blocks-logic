using Blocks.Genesis.Entities.Logic;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Scheduler.DomainService.Repositories;
using Scheduler.DomainService.Services;
using Scheduler.DomainService.Utils;

namespace XUnitTest.Scheduler
{
    /// <summary><see cref="ScheduleExecutionRecorder"/>: one best-effort insert that never throws (C1, C6).</summary>
    public class ScheduleExecutionRecorderTests
    {
        private readonly Mock<IScheduleExecutionRepository> _repository = new();
        private readonly Mock<ILogger<ScheduleExecutionRecorder>> _logger = new();

        private static ScheduleExecution Row() => new() { ItemId = "e1", ScheduleId = "s1", TenantId = "t1" };

        private ScheduleExecutionRecorder Create(bool enabled = true, TimeSpan? timeout = null) =>
            timeout is { } t
                ? new ScheduleExecutionRecorder(_repository.Object, Options.Create(new ScheduleExecutionHistoryOptions { Enabled = enabled }), _logger.Object, t)
                : new ScheduleExecutionRecorder(_repository.Object, Options.Create(new ScheduleExecutionHistoryOptions { Enabled = enabled }), _logger.Object);

        private void VerifyOneWarning() =>
            _logger.Verify(l => l.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, _) => v.ToString()!.Contains("Schedule execution history write failed for schedule s1 (tenant t1).")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.Once);

        [Fact]
        public async Task RecordAsync_Inserts()
        {
            var row = Row();

            await Create().RecordAsync(row);

            _repository.Verify(r => r.InsertAsync(row, It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task RecordAsync_InsertThrows_SwallowsAndWarnsOnce()
        {
            _repository.Setup(r => r.InsertAsync(It.IsAny<ScheduleExecution>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("mongo down"));

            await Create().Invoking(r => r.RecordAsync(Row())).Should().NotThrowAsync();

            VerifyOneWarning();
        }

        [Fact]
        public async Task RecordAsync_InsertHangs_GivesUpAtTheTimeout()
        {
            // An insert that ignores its token: WaitAsync still bounds the call.
            _repository.Setup(r => r.InsertAsync(It.IsAny<ScheduleExecution>(), It.IsAny<CancellationToken>()))
                .Returns(Task.Delay(Timeout.Infinite));

            var started = DateTime.UtcNow;
            await Create(timeout: TimeSpan.FromMilliseconds(100)).Invoking(r => r.RecordAsync(Row())).Should().NotThrowAsync();

            (DateTime.UtcNow - started).Should().BeLessThan(TimeSpan.FromSeconds(5));
            VerifyOneWarning();
        }

        [Fact]
        public async Task RecordAsync_PassesATokenThatCancelsAtTheTimeout()
        {
            CancellationToken seen = default;
            _repository.Setup(r => r.InsertAsync(It.IsAny<ScheduleExecution>(), It.IsAny<CancellationToken>()))
                .Returns<ScheduleExecution, CancellationToken>((_, token) => { seen = token; return Task.Delay(Timeout.Infinite, token); });

            await Create(timeout: TimeSpan.FromMilliseconds(50)).RecordAsync(Row());

            seen.IsCancellationRequested.Should().BeTrue();
        }

        [Fact]
        public void DefaultTimeout_IsFiveSeconds()
        {
            Create().InsertTimeout.Should().Be(TimeSpan.FromSeconds(5));
        }

        [Fact]
        public async Task RecordAsync_Disabled_WritesNothing()
        {
            await Create(enabled: false).RecordAsync(Row());

            _repository.Verify(r => r.InsertAsync(It.IsAny<ScheduleExecution>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public void Options_DefaultToEnabled()
        {
            new ScheduleExecutionHistoryOptions().Enabled.Should().BeTrue();
            ScheduleExecutionHistoryOptions.SectionName.Should().Be("Scheduler:ExecutionHistory");
        }
    }
}
