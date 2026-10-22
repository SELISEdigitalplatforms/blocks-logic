using FluentAssertions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Moq;
using Scheduler.DomainService.Services;

namespace XUnitTest.Scheduler
{
    /// <summary>
    /// The startup reseed is retried 5 times with 2, 4, 8 and 16 s between attempts; if every attempt fails,
    /// the process exits with code 1 so it is restarted. Stopping the host during a wait ends it quietly.
    /// </summary>
    public sealed class SchedulerReseedHostedServiceTests : IDisposable
    {
        private readonly Mock<IScheduleJobRegistry> _registry = new();
        private readonly Mock<IHostApplicationLifetime> _lifetime = new();
        private readonly Mock<ILogger<SchedulerReseedHostedService>> _logger = new();
        private readonly int _exitCodeBefore = Environment.ExitCode;

        public void Dispose() => Environment.ExitCode = _exitCodeBefore;

        private SchedulerReseedHostedService Create(TimeProvider clock) =>
            new(_registry.Object, _lifetime.Object, clock, _logger.Object);

        private static async Task RunAsync(SchedulerReseedHostedService service)
        {
            await service.StartAsync(CancellationToken.None);
            await service.ExecuteTask!.WaitAsync(TimeSpan.FromSeconds(10));
        }

        [Fact]
        public async Task A_reseed_that_works_at_once_registers_and_keeps_running()
        {
            var clock = new RecordingClock(fire: true);

            await RunAsync(Create(clock));

            _registry.Verify(r => r.RegisterAllAsync(), Times.Once);
            clock.Delays.Should().BeEmpty();
            _lifetime.Verify(l => l.StopApplication(), Times.Never);
            VerifyLogged(LogLevel.Information, Times.AtLeastOnce());
        }

        [Fact]
        public async Task Two_failures_then_a_success_registers_without_stopping()
        {
            var clock = new RecordingClock(fire: true);
            _registry.SetupSequence(r => r.RegisterAllAsync())
                .ThrowsAsync(new TimeoutException("mongo down"))
                .ThrowsAsync(new TimeoutException("mongo down"))
                .Returns(Task.CompletedTask);

            await RunAsync(Create(clock));

            _registry.Verify(r => r.RegisterAllAsync(), Times.Exactly(3));
            clock.Delays.Should().Equal(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4));
            VerifyLogged(LogLevel.Warning, Times.Exactly(2));
            VerifyLogged(LogLevel.Error, Times.Never());
            _lifetime.Verify(l => l.StopApplication(), Times.Never);
            Environment.ExitCode.Should().Be(_exitCodeBefore);
        }

        [Fact]
        public async Task Five_failures_stop_the_host_with_exit_code_1()
        {
            var clock = new RecordingClock(fire: true);
            _registry.Setup(r => r.RegisterAllAsync()).ThrowsAsync(new TimeoutException("mongo down"));

            await RunAsync(Create(clock));

            _registry.Verify(r => r.RegisterAllAsync(), Times.Exactly(5));
            clock.Delays.Should().Equal(
                TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(8), TimeSpan.FromSeconds(16));
            VerifyLogged(LogLevel.Warning, Times.Exactly(4));
            VerifyLogged(LogLevel.Error, Times.Once());
            _lifetime.Verify(l => l.StopApplication(), Times.Once);
            Environment.ExitCode.Should().Be(1);
        }

        [Fact]
        public async Task Stopping_during_a_wait_ends_the_retries_quietly()
        {
            var clock = new RecordingClock(fire: false);
            _registry.Setup(r => r.RegisterAllAsync()).ThrowsAsync(new TimeoutException("mongo down"));
            var service = Create(clock);

            await service.StartAsync(CancellationToken.None);
            await clock.FirstDelayRequested.WaitAsync(TimeSpan.FromSeconds(10));
            await service.StopAsync(CancellationToken.None);

            service.ExecuteTask!.IsCompletedSuccessfully.Should().BeTrue();
            _registry.Verify(r => r.RegisterAllAsync(), Times.Once);
            VerifyLogged(LogLevel.Error, Times.Never());
            _lifetime.Verify(l => l.StopApplication(), Times.Never);
            Environment.ExitCode.Should().Be(_exitCodeBefore);
        }

        private void VerifyLogged(LogLevel level, Times times) =>
            _logger.Verify(l => l.Log(
                level,
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()), times);

        /// <summary>
        /// Records every wait the service asks for. With <c>fire</c> the wait ends at once (on the thread pool,
        /// as a real timer would); without it the wait never ends on its own.
        /// </summary>
        private sealed class RecordingClock(bool fire) : TimeProvider
        {
            private readonly TaskCompletionSource _firstDelay = new(TaskCreationOptions.RunContinuationsAsynchronously);

            public List<TimeSpan> Delays { get; } = [];

            public Task FirstDelayRequested => _firstDelay.Task;

            public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
            {
                lock (Delays)
                {
                    Delays.Add(dueTime);
                }
                _firstDelay.TrySetResult();
                if (fire)
                {
                    ThreadPool.QueueUserWorkItem(_ => callback(state));
                }
                return new IdleTimer();
            }

            private sealed class IdleTimer : ITimer
            {
                public bool Change(TimeSpan dueTime, TimeSpan period) => true;

                public void Dispose()
                {
                }

                public ValueTask DisposeAsync() => ValueTask.CompletedTask;
            }
        }
    }
}
