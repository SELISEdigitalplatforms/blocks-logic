using Blocks.Genesis;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Scheduler.DomainService.Dtos.Requests;
using Scheduler.DomainService.Entities;
using Scheduler.DomainService.Enums;
using Scheduler.DomainService.Models;
using Scheduler.DomainService.Repositories;
using Scheduler.DomainService.Services;
using XUnitTest.TestHelpers;

namespace XUnitTest.Scheduler
{
    /// <summary>
    /// Unit tests for <see cref="ScheduleService"/> webhook-only create/update mapping
    /// and the Internal-schedule guards on update/delete. Repository and message client
    /// are mocked; no Mongo/Hangfire infrastructure is exercised here.
    /// </summary>
    public class ScheduleServiceTests : IDisposable
    {
        private readonly Mock<IScheduleRepository> _scheduleRepository = new();
        private readonly Mock<IMessageClient> _messageClient = new();
        private readonly ScheduleService _service;

        public ScheduleServiceTests()
        {
            TestBlocksContext.Set("tenant-sched", "user-sched");
            _service = new ScheduleService(
                _scheduleRepository.Object,
                _messageClient.Object,
                Mock.Of<ILogger<ScheduleService>>());
        }

        public void Dispose()
        {
            TestBlocksContext.Clear();
        }

        private static CreateScheduleRequestDto CreateWebhookRequest(
            string url = "https://webhook.example.com/hook",
            string method = "POST",
            WebhookConfiguration? webhook = null,
            bool includeDefaultWebhook = true) => new()
        {
            Name = "Test webhook schedule",
            Payload = "{}",
            CronExpression = "0 9 * * MON-FRI",
            StartDate = DateTime.UtcNow.AddDays(-1),
            EndDate = DateTime.UtcNow.AddDays(1),
            Webhook = includeDefaultWebhook
                ? (webhook ?? new WebhookConfiguration { Url = url, Method = method })
                : webhook,
        };

        // ---------- Create ----------

        [Fact]
        public async Task Create_WithDescription_PersistsTrimmedNameAndDescription()
        {
            var request = CreateWebhookRequest();
            request.Name = "  Daily sync  ";
            request.Description = "  Fires every weekday  ";

            var result = await _service.CreateScheduleAsync(request);

            result.IsSuccess.Should().BeTrue();
            _scheduleRepository.Verify(r => r.CreateAsync(It.Is<Schedule>(s =>
                s.Name == "Daily sync" &&
                s.Description == "Fires every weekday")), Times.Once);
        }

        [Fact]
        public async Task Create_WithoutDescription_PersistsNullDescription()
        {
            var request = CreateWebhookRequest();
            request.Description = null;

            var result = await _service.CreateScheduleAsync(request);

            result.IsSuccess.Should().BeTrue();
            _scheduleRepository.Verify(r => r.CreateAsync(It.Is<Schedule>(s =>
                s.Name == "Test webhook schedule" &&
                s.Description == null)), Times.Once);
        }

        [Fact]
        public async Task Create_WebhookTrigger_PersistsWebhookConfiguration()
        {
            var request = CreateWebhookRequest();

            var result = await _service.CreateScheduleAsync(request);

            result.IsSuccess.Should().BeTrue();
            _scheduleRepository.Verify(r => r.CreateAsync(It.Is<Schedule>(s =>
                s.TriggerType == ScheduleTriggerType.Webhook &&
                s.Webhook != null &&
                s.Webhook.Url == "https://webhook.example.com/hook" &&
                s.Queue == null)), Times.Once);
        }

        [Fact]
        public async Task Create_AlwaysSetsKindToApplication()
        {
            var request = CreateWebhookRequest();

            var result = await _service.CreateScheduleAsync(request);

            result.IsSuccess.Should().BeTrue();
            _scheduleRepository.Verify(r => r.CreateAsync(It.Is<Schedule>(s => s.Kind == ScheduleKind.Application)), Times.Once);
        }

        [Fact]
        public async Task Create_RepositoryFailure_ReturnsCreateFailed()
        {
            var request = CreateWebhookRequest();
            _scheduleRepository.Setup(r => r.CreateAsync(It.IsAny<Schedule>()))
                .ThrowsAsync(new InvalidOperationException("boom"));

            var result = await _service.CreateScheduleAsync(request);

            result.IsSuccess.Should().BeFalse();
            result.Errors.Should().ContainKey("create_failed");
        }

        // ---------- Update: webhook-only mapping + Internal guard ----------

        [Fact]
        public async Task Update_ApplicationSchedule_ConvertsToWebhookAndClearsQueue()
        {
            var request = new UpdateScheduleRequestDto
            {
                ItemId = "sched-app",
                Name = "Schedule",
                Payload = "{}",
                CronExpression = "0 9 * * MON-FRI",
                IsActive = true,
                Webhook = new WebhookConfiguration { Url = "https://webhook.example.com/hook", Method = "POST" },
            };
            Schedule? captured = null;
            _scheduleRepository.Setup(r => r.GetByIdAsync("sched-app", It.IsAny<string>()))
                .ReturnsAsync(new Schedule
                {
                    ItemId = "sched-app",
                    Kind = ScheduleKind.Application,
                    TriggerType = ScheduleTriggerType.Queue,
                    Queue = new QueueConfiguration { QueueName = "orders" },
                });
            _scheduleRepository.Setup(r => r.UpdateAsync(It.IsAny<Schedule>()))
                .Callback<Schedule>(s => captured = s)
                .Returns(Task.CompletedTask);

            var result = await _service.UpdateScheduleAsync(request);

            result.IsSuccess.Should().BeTrue();
            captured.Should().NotBeNull();
            captured!.TriggerType.Should().Be(ScheduleTriggerType.Webhook);
            captured.Queue.Should().BeNull();
            captured.Webhook!.Url.Should().Be("https://webhook.example.com/hook");
        }

        [Fact]
        public async Task Update_InternalSchedule_ReturnsInternalScheduleError()
        {
            var request = new UpdateScheduleRequestDto
            {
                ItemId = "sched-internal",
                Name = "Internal schedule",
                Payload = "{}",
                CronExpression = "0 9 * * MON-FRI",
                IsActive = true,
                Webhook = new WebhookConfiguration { Url = "https://webhook.example.com/hook", Method = "POST" },
            };
            _scheduleRepository.Setup(r => r.GetByIdAsync("sched-internal", It.IsAny<string>()))
                .ReturnsAsync(new Schedule { ItemId = "sched-internal", Kind = ScheduleKind.Internal });

            var result = await _service.UpdateScheduleAsync(request);

            result.IsSuccess.Should().BeFalse();
            result.Errors.Should().ContainKey("internal_schedule");
            _scheduleRepository.Verify(r => r.UpdateAsync(It.IsAny<Schedule>()), Times.Never);
        }

        [Fact]
        public async Task Update_UnknownSchedule_ReturnsNotFound()
        {
            var request = new UpdateScheduleRequestDto
            {
                ItemId = "sched-missing",
                Name = "Schedule",
                Payload = "{}",
                CronExpression = "0 9 * * MON-FRI",
                IsActive = true,
                Webhook = new WebhookConfiguration { Url = "https://webhook.example.com/hook", Method = "POST" },
            };
            _scheduleRepository.Setup(r => r.GetByIdAsync("sched-missing", It.IsAny<string>()))
                .ReturnsAsync((Schedule?)null);

            var result = await _service.UpdateScheduleAsync(request);

            result.IsSuccess.Should().BeFalse();
            result.Errors.Should().ContainKey("schedule_not_found");
        }

        // ---------- Delete: Internal guard ----------

        [Fact]
        public async Task Delete_InternalSchedule_ReturnsInternalScheduleError()
        {
            _scheduleRepository.Setup(r => r.GetByIdAsync("sched-internal", It.IsAny<string>()))
                .ReturnsAsync(new Schedule { ItemId = "sched-internal", Kind = ScheduleKind.Internal });

            var result = await _service.DeleteScheduleAsync(new DeleteScheduleRequestDto { ItemId = "sched-internal" });

            result.IsSuccess.Should().BeFalse();
            result.Errors.Should().ContainKey("internal_schedule");
            _scheduleRepository.Verify(r => r.DeleteAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        }

    }
}
