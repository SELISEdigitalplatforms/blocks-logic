using Blocks.Genesis;
using Blocks.Genesis.Entities.Logic;
using FluentAssertions;
using Hangfire;
using Hangfire.Common;
using Microsoft.Extensions.Logging;
using Moq;
using Scheduler.DomainService.Dtos;
using Scheduler.DomainService.Events;
using Scheduler.DomainService.Repositories;
using Scheduler.DomainService.Services;

namespace XUnitTest.Scheduler
{
    /// <summary><see cref="ScheduleJobRegistry"/>: the time zone Hangfire gets, and the unresolvable-zone paths (SPEC6 §5.4).</summary>
    public class ScheduleJobRegistryTests : IDisposable
    {
        private readonly Mock<IScheduleRepository> _repository = new();
        private readonly Mock<IRecurringJobManager> _jobs = new();
        private readonly SchedulePublisherService _publisher;
        private readonly ScheduleJobRegistry _registry;
        private readonly List<(string Id, string Cron, RecurringJobOptions Options)> _registered = [];

        public ScheduleJobRegistryTests()
        {
            _publisher = new SchedulePublisherService(
                Mock.Of<ILogger<SchedulePublisherService>>(), _repository.Object, Mock.Of<IMessageClient>(), Mock.Of<ITenants>());
            _jobs.Setup(j => j.AddOrUpdate(It.IsAny<string>(), It.IsAny<Job>(), It.IsAny<string>(), It.IsAny<RecurringJobOptions>()))
                .Callback<string, Job, string, RecurringJobOptions>((id, _, cron, options) => _registered.Add((id, cron, options)));
            _registry = new ScheduleJobRegistry(_repository.Object, _publisher, _jobs.Object, Mock.Of<ILogger<ScheduleJobRegistry>>());
        }

        public void Dispose() => _publisher.Dispose();

        private static Schedule Schedule(string id, string? timeZone, bool isActive = true) => new()
        {
            ItemId = id,
            IsActive = isActive,
            CronExpression = "0 9 * * *",
            TimeZone = timeZone,
            Kind = ScheduleKind.Application,
        };

        private void Stored(Schedule schedule) =>
            _repository.Setup(r => r.GetByIdAsync(schedule.ItemId, "t1")).ReturnsAsync(schedule);

        [Fact]
        public async Task Upsert_RegistersInTheStoredZone()
        {
            Stored(Schedule("s1", "Asia/Dhaka"));

            await _registry.ApplyUpsertAsync(new ScheduleJobUpsertedEvent { ItemId = "s1", TenantId = "t1" });

            var job = _registered.Should().ContainSingle().Subject;
            job.Id.Should().Be("s1-t1");
            job.Cron.Should().Be("0 9 * * *");
            job.Options.TimeZone.GetUtcOffset(new DateTime(2026, 10, 26, 0, 0, 0, DateTimeKind.Utc)).Should().Be(TimeSpan.FromHours(6));
        }

        [Fact]
        public async Task Upsert_NullZone_RegistersInUtc_AsToday()
        {
            Stored(Schedule("s1", null));

            await _registry.ApplyUpsertAsync(new ScheduleJobUpsertedEvent { ItemId = "s1", TenantId = "t1" });

            _registered.Should().ContainSingle().Which.Options.TimeZone.Should().Be(TimeZoneInfo.Utc);
        }

        [Fact]
        public async Task Upsert_UnknownZone_RemovesTheJob_NeverFallsBackToUtc_AndDoesNotThrow()
        {
            Stored(Schedule("s1", "Mars/Olympus"));

            await _registry.Invoking(r => r.ApplyUpsertAsync(new ScheduleJobUpsertedEvent { ItemId = "s1", TenantId = "t1" }))
                .Should().NotThrowAsync();

            _registered.Should().BeEmpty();
            _jobs.Verify(j => j.RemoveIfExists("s1-t1"), Times.Once);
        }

        [Fact]
        public async Task Upsert_Inactive_RemovesTheJob()
        {
            Stored(Schedule("s1", "Asia/Dhaka", isActive: false));

            await _registry.ApplyUpsertAsync(new ScheduleJobUpsertedEvent { ItemId = "s1", TenantId = "t1" });

            _registered.Should().BeEmpty();
            _jobs.Verify(j => j.RemoveIfExists("s1-t1"), Times.Once);
        }

        [Fact]
        public async Task Delete_RemovesTheJob()
        {
            await _registry.ApplyDeleteAsync(new ScheduleJobDeletedEvent { ItemId = "s1", TenantId = "t1" });

            _jobs.Verify(j => j.RemoveIfExists("s1-t1"), Times.Once);
        }

        [Fact]
        public async Task Reseed_SkipsAnUnknownZone_AndRegistersTheRest()
        {
            _repository.Setup(r => r.GetSchedulesFromAllTenantsAsync()).ReturnsAsync(
            [
                new SchedularDto
                {
                    TenantId = "t1",
                    Schedules = [Schedule("s1", null), Schedule("s2", "Mars/Olympus"), Schedule("s3", "America/New_York")],
                },
            ]);

            await _registry.RegisterAllAsync();

            _registered.Select(j => j.Id).Should().Equal("s1-t1", "s3-t1");
            _registered[0].Options.TimeZone.Should().Be(TimeZoneInfo.Utc);
            _registered[1].Options.TimeZone.Id.Should().Be(TimeZoneInfo.FindSystemTimeZoneById("America/New_York").Id);
        }
    }
}
