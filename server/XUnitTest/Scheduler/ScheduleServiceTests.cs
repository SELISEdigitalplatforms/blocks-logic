using Blocks.Genesis;
using Blocks.Genesis.DataAccess.Rules;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Scheduler.DomainService.Dtos.Requests;
using Scheduler.DomainService.Events;
using Scheduler.DomainService.Models;
using Scheduler.DomainService.Repositories;
using Scheduler.DomainService.Services;
using SchedulerConstants = Scheduler.DomainService.Utils.SchedulerConstants;
using XUnitTest.TestHelpers;
using Blocks.Genesis.Entities.Logic;

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
        public async Task Create_RefusedByAValidationRule_ReturnsItsMessage_AndQueuesNothing()
        {
            var refused = new DataValidationResult();
            refused.AddError("Name", "Name must be at most 10 characters", "MaxLength");
            _scheduleRepository.Setup(r => r.CreateAsync(It.IsAny<Schedule>())).ThrowsAsync(new DataValidationException(refused));

            var result = await _service.CreateScheduleAsync(CreateWebhookRequest());

            result.IsSuccess.Should().BeFalse();
            result.Errors.Should().Equal(new Dictionary<string, string> { ["Name"] = "Name must be at most 10 characters" });
            _messageClient.VerifyNoOtherCalls();
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

        // ---------- 5-field-only Application crons (D2) ----------

        [Theory]
        [InlineData("0 */5 * * * *")]
        [InlineData("*/15 * * * * *")]
        [InlineData("not-a-cron")]
        [InlineData("")]
        public async Task Create_ApplicationSchedule_RejectsSixFieldOrInvalidCron(string cron)
        {
            var request = CreateWebhookRequest();
            request.CronExpression = cron;

            var result = await _service.CreateScheduleAsync(request);

            result.IsSuccess.Should().BeFalse();
            result.Errors.Should().Equal(new Dictionary<string, string> { ["validation_failed"] = "Cron expression is not valid" });
            _scheduleRepository.Verify(r => r.CreateAsync(It.IsAny<Schedule>()), Times.Never);
        }

        [Fact]
        public async Task Create_ApplicationSchedule_AcceptsFiveFieldCron()
        {
            var request = CreateWebhookRequest();
            request.CronExpression = "* * * * *";

            (await _service.CreateScheduleAsync(request)).IsSuccess.Should().BeTrue();
        }

        [Fact]
        public async Task Update_ApplicationSchedule_RejectsSixFieldCron()
        {
            var result = await _service.UpdateScheduleAsync(new UpdateScheduleRequestDto
            {
                ItemId = "sched-app",
                Name = "Schedule",
                Payload = "{}",
                CronExpression = "0 0 9 * * *",
                IsActive = true,
                Webhook = new WebhookConfiguration { Url = "https://webhook.example.com/hook", Method = "POST" },
            });

            result.IsSuccess.Should().BeFalse();
            result.Errors.Should().Equal(new Dictionary<string, string> { ["validation_failed"] = "Cron expression is not valid" });
            _scheduleRepository.Verify(r => r.UpdateAsync(It.IsAny<Schedule>()), Times.Never);
        }

        [Theory]
        [InlineData("* * * * *", true)]
        [InlineData("0 9 * * MON-FRI", true)]
        [InlineData("0 */5 * * * *", false)]
        [InlineData(" ", false)]
        public void IsValidApplicationCronExpression_IsStandardFormatOnly(string cron, bool valid)
        {
            global::Scheduler.DomainService.Utils.Helper.IsValidApplicationCronExpression(cron).Should().Be(valid);
            // The general (workflow) validator still accepts 6-field crons.
            if (cron == "0 */5 * * * *")
            {
                global::Scheduler.DomainService.Utils.Helper.IsValidCronExpression(cron).Should().BeTrue();
            }
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

        // ---------- CreateWorkflowScheduleAsync ----------

        [Fact]
        public async Task CreateWorkflowSchedule_PersistsInternalQueueSchedule()
        {
            var result = await _service.CreateWorkflowScheduleAsync(new CreateWorkflowScheduleRequest
            {
                WorkflowId = "wf-1",
                NodeId = "node-1",
                TenantId = "tenant-sched",
                CronExpression = "30 */10 * * * *",
                StartDate = DateTime.UtcNow.AddDays(-1),
                EndDate = DateTime.UtcNow.AddDays(30),
            });

            result.IsSuccess.Should().BeTrue();
            _scheduleRepository.Verify(r => r.CreateAsync(It.Is<Schedule>(s =>
                s.Kind == ScheduleKind.Internal &&
                s.TriggerType == ScheduleTriggerType.Queue &&
                s.Queue != null &&
                s.Queue.QueueName == SchedulerConstants.WorkflowSchedulerTriggerQueue &&
                s.Name == "wf-tenant-sched-wf-1-node-1" &&
                s.IsActive)), Times.Once);
        }

        [Fact]
        public async Task CreateWorkflowSchedule_PayloadContainsWorkflowTriggerTenantAndCron()
        {
            var result = await _service.CreateWorkflowScheduleAsync(new CreateWorkflowScheduleRequest
            {
                WorkflowId = "wf-1",
                NodeId = "node-1",
                TenantId = "tenant-sched",
                CronExpression = "*/15 * * * * *",
                StartDate = DateTime.UtcNow.AddDays(-1),
                EndDate = DateTime.UtcNow.AddDays(30),
            });

            result.IsSuccess.Should().BeTrue();
            _scheduleRepository.Verify(r => r.CreateAsync(It.Is<Schedule>(s =>
                s.Payload.Contains("\"workflowId\":\"wf-1\"") &&
                s.Payload.Contains("\"triggerId\":\"node-1\"") &&
                s.Payload.Contains("\"tenantId\":\"tenant-sched\"") &&
                s.Payload.Contains("\"cronExpression\":\"*/15 * * * * *\""))), Times.Once);
        }

        [Fact]
        public async Task CreateWorkflowSchedule_GeneratesGuidItemId_ReturnsItInResponse()
        {
            var result = await _service.CreateWorkflowScheduleAsync(new CreateWorkflowScheduleRequest
            {
                WorkflowId = "wf-1",
                NodeId = "node-1",
                TenantId = "tenant-sched",
                CronExpression = "0 9 * * *",
            });

            result.IsSuccess.Should().BeTrue();
            Guid.TryParse(result.ItemId, out _).Should().BeTrue();
            _scheduleRepository.Verify(r => r.CreateAsync(It.Is<Schedule>(s => s.ItemId == result.ItemId)), Times.Once);
        }

        [Fact]
        public async Task CreateWorkflowSchedule_InvalidCron_ReturnsValidationError()
        {
            var result = await _service.CreateWorkflowScheduleAsync(new CreateWorkflowScheduleRequest
            {
                WorkflowId = "wf-1",
                NodeId = "node-1",
                TenantId = "tenant-sched",
                CronExpression = "not-a-cron",
            });

            result.IsSuccess.Should().BeFalse();
            result.Errors.Should().ContainKey("validation_failed");
            _scheduleRepository.Verify(r => r.CreateAsync(It.IsAny<Schedule>()), Times.Never);
        }

        [Fact]
        public async Task CreateWorkflowSchedule_EnqueuesUpsertedEventWithTenantId()
        {
            var result = await _service.CreateWorkflowScheduleAsync(new CreateWorkflowScheduleRequest
            {
                WorkflowId = "wf-1",
                NodeId = "node-1",
                TenantId = "tenant-sched",
                CronExpression = "0 9 * * *",
            });

            result.IsSuccess.Should().BeTrue();
            _messageClient.Verify(m => m.SendToConsumerAsync(It.Is<ConsumerMessage<ScheduleJobUpsertedEvent>>(msg =>
                msg.ConsumerName == SchedulerConstants.ScheduleJobRegistryQueueName &&
                msg.Payload.ItemId == result.ItemId &&
                msg.Payload.TenantId == "tenant-sched")), Times.Once);
        }

        [Fact]
        public async Task CreateWorkflowSchedule_MissingWorkflowId_ReturnsValidationError()
        {
            var result = await _service.CreateWorkflowScheduleAsync(new CreateWorkflowScheduleRequest
            {
                NodeId = "node-1",
                TenantId = "tenant-sched",
                CronExpression = "0 9 * * *",
            });

            result.IsSuccess.Should().BeFalse();
            result.Errors.Should().ContainKey("validation_failed");
        }

        // ---------- DeleteWorkflowSchedulesAsync ----------

        [Fact]
        public async Task DeleteWorkflowSchedules_DeletesEachAndEnqueuesDeletedEvents()
        {
            _scheduleRepository.Setup(r => r.GetByIdAsync("sched-a", It.IsAny<string>()))
                .ReturnsAsync(new Schedule { ItemId = "sched-a", Kind = ScheduleKind.Internal });
            _scheduleRepository.Setup(r => r.GetByIdAsync("sched-b", It.IsAny<string>()))
                .ReturnsAsync(new Schedule { ItemId = "sched-b", Kind = ScheduleKind.Internal });
            _scheduleRepository.Setup(r => r.DeleteAsync(It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync(true);

            var result = await _service.DeleteWorkflowSchedulesAsync(["sched-a", "sched-b"]);

            result.IsSuccess.Should().BeTrue();
            _scheduleRepository.Verify(r => r.DeleteAsync("sched-a", It.IsAny<string>()), Times.Once);
            _scheduleRepository.Verify(r => r.DeleteAsync("sched-b", It.IsAny<string>()), Times.Once);
            _messageClient.Verify(m => m.SendToConsumerAsync(It.Is<ConsumerMessage<ScheduleJobDeletedEvent>>(msg =>
                msg.Payload.ItemId == "sched-a" || msg.Payload.ItemId == "sched-b")), Times.Exactly(2));
        }

        [Fact]
        public async Task DeleteWorkflowSchedules_ToleratesMissingIds()
        {
            _scheduleRepository.Setup(r => r.GetByIdAsync("gone", It.IsAny<string>()))
                .ReturnsAsync((Schedule?)null);

            var result = await _service.DeleteWorkflowSchedulesAsync(["gone"]);

            result.IsSuccess.Should().BeTrue();
            _scheduleRepository.Verify(r => r.DeleteAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
            _messageClient.Verify(m => m.SendToConsumerAsync(It.IsAny<ConsumerMessage<ScheduleJobDeletedEvent>>()), Times.Never);
        }

        [Fact]
        public async Task DeleteWorkflowSchedules_DeleteFailure_ReturnsError()
        {
            _scheduleRepository.Setup(r => r.GetByIdAsync("sched-a", It.IsAny<string>()))
                .ReturnsAsync(new Schedule { ItemId = "sched-a", Kind = ScheduleKind.Internal });
            _scheduleRepository.Setup(r => r.DeleteAsync("sched-a", It.IsAny<string>()))
                .ReturnsAsync(false);

            var result = await _service.DeleteWorkflowSchedulesAsync(["sched-a"]);

            result.IsSuccess.Should().BeFalse();
            result.Errors.Should().ContainKey("delete_failed");
        }

        // ---------- Time zone (SPEC6) ----------

        private sealed class FixedClock(DateTimeOffset now) : TimeProvider
        {
            public override DateTimeOffset GetUtcNow() => now;
        }

        private static readonly DateTime Now = new(2026, 10, 26, 10, 10, 0, DateTimeKind.Utc);

        private ScheduleService ServiceAt(DateTime utcNow) => new(
            _scheduleRepository.Object, _messageClient.Object, Mock.Of<ILogger<ScheduleService>>(),
            timeProvider: new FixedClock(new DateTimeOffset(utcNow)));

        private UpdateScheduleRequestDto UpdateRequest(string? timeZone) => new()
        {
            ItemId = "sched-app",
            Name = "Schedule",
            Payload = "{}",
            CronExpression = "0 9 * * *",
            IsActive = true,
            TimeZone = timeZone,
            Webhook = new WebhookConfiguration { Url = "https://webhook.example.com/hook", Method = "POST" },
        };

        [Theory]
        [InlineData("Asia/Dhaka", "Asia/Dhaka")]
        [InlineData(null, null)]
        [InlineData("", null)]
        [InlineData("UTC", null)]
        [InlineData("Etc/UTC", null)]
        public async Task Create_StoresTheNormalisedTimeZone(string? input, string? stored)
        {
            var request = CreateWebhookRequest();
            request.TimeZone = input;

            var result = await _service.CreateScheduleAsync(request);

            result.IsSuccess.Should().BeTrue();
            _scheduleRepository.Verify(r => r.CreateAsync(It.Is<Schedule>(s => s.TimeZone == stored)), Times.Once);
        }

        [Theory]
        [InlineData("Mars/Olympus")]
        [InlineData("Bangladesh Standard Time")]
        public async Task Create_UnknownTimeZone_IsRefused_AndNothingIsWritten(string zone)
        {
            var request = CreateWebhookRequest();
            request.TimeZone = zone;

            var result = await _service.CreateScheduleAsync(request);

            result.IsSuccess.Should().BeFalse();
            result.Errors.Should().Equal(new Dictionary<string, string> { ["validation_failed"] = "Time zone is not recognised" });
            _scheduleRepository.Verify(r => r.CreateAsync(It.IsAny<Schedule>()), Times.Never);
            _messageClient.Verify(m => m.SendToConsumerAsync(It.IsAny<ConsumerMessage<ScheduleJobUpsertedEvent>>()), Times.Never);
        }

        [Fact]
        public async Task Create_InvalidCron_IsReportedBeforeAnInvalidTimeZone()
        {
            var request = CreateWebhookRequest();
            request.CronExpression = "0 0 9 * * *";
            request.TimeZone = "Mars/Olympus";

            var result = await _service.CreateScheduleAsync(request);

            result.Errors.Should().Equal(new Dictionary<string, string> { ["validation_failed"] = "Cron expression is not valid" });
        }

        [Theory]
        [InlineData("Asia/Dhaka", "Asia/Dhaka")]
        [InlineData(null, null)]
        public async Task Update_ReplacesTheTimeZone_OmittingItStoresUtc(string? input, string? stored)
        {
            _scheduleRepository.Setup(r => r.GetByIdAsync("sched-app", It.IsAny<string>()))
                .ReturnsAsync(new Schedule { ItemId = "sched-app", Kind = ScheduleKind.Application, TimeZone = "America/New_York" });

            var result = await _service.UpdateScheduleAsync(UpdateRequest(input));

            result.IsSuccess.Should().BeTrue();
            _scheduleRepository.Verify(r => r.UpdateAsync(It.Is<Schedule>(s => s.TimeZone == stored)), Times.Once);
        }

        [Fact]
        public async Task Update_UnknownTimeZone_LeavesTheScheduleAndJobUnchanged()
        {
            var result = await _service.UpdateScheduleAsync(UpdateRequest("Mars/Olympus"));

            result.Errors.Should().Equal(new Dictionary<string, string> { ["validation_failed"] = "Time zone is not recognised" });
            _scheduleRepository.Verify(r => r.GetByIdAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
            _scheduleRepository.Verify(r => r.UpdateAsync(It.IsAny<Schedule>()), Times.Never);
            _messageClient.Verify(m => m.SendToConsumerAsync(It.IsAny<ConsumerMessage<ScheduleJobUpsertedEvent>>()), Times.Never);
        }

        [Fact]
        public async Task CreateWorkflowSchedule_HasNoTimeZone()
        {
            await _service.CreateWorkflowScheduleAsync(new CreateWorkflowScheduleRequest
            {
                WorkflowId = "wf-1",
                NodeId = "node-1",
                TenantId = "tenant-sched",
                CronExpression = "30 */10 * * * *",
            });

            _scheduleRepository.Verify(r => r.CreateAsync(It.Is<Schedule>(s => s.TimeZone == null)), Times.Once);
        }

        [Fact]
        public async Task GetSchedules_MapsTimeZoneAndNextRun()
        {
            _scheduleRepository.Setup(r => r.GetAllAsync(It.IsAny<string>(), 0, 10, ScheduleKind.Application)).ReturnsAsync(
            ([
                new Schedule { ItemId = "dhaka", CronExpression = "0 9 * * *", IsActive = true, TimeZone = "Asia/Dhaka" },
                new Schedule { ItemId = "utc", CronExpression = "0 9 * * *", IsActive = true },
                new Schedule { ItemId = "inactive", CronExpression = "0 9 * * *", IsActive = false, TimeZone = "Asia/Dhaka" },
                new Schedule { ItemId = "ended", CronExpression = "0 9 * * *", IsActive = true, EndDate = Now.AddHours(1) },
                new Schedule { ItemId = "broken", CronExpression = "not-a-cron", IsActive = true },
                new Schedule { ItemId = "lost", CronExpression = "0 9 * * *", IsActive = true, TimeZone = "Mars/Olympus" },
            ], 6));

            var result = await ServiceAt(Now).GetSchedulesAsync(new GetSchedulesRequestDto { SearchKey = "", PageNumber = 0, PageSize = 10 });

            var byId = result.Data!.ToDictionary(s => s.ItemId);
            byId["dhaka"].TimeZone.Should().Be("Asia/Dhaka");
            byId["dhaka"].NextRunAtUtc.Should().Be(new DateTime(2026, 10, 27, 3, 0, 0, DateTimeKind.Utc));
            byId["utc"].TimeZone.Should().BeNull();
            byId["utc"].NextRunAtUtc.Should().Be(new DateTime(2026, 10, 27, 9, 0, 0, DateTimeKind.Utc));
            byId["inactive"].NextRunAtUtc.Should().BeNull();
            byId["ended"].NextRunAtUtc.Should().BeNull();
            byId["broken"].NextRunAtUtc.Should().BeNull();
            byId["lost"].NextRunAtUtc.Should().BeNull();
        }

        [Fact]
        public async Task PreviewScheduleRuns_ReturnsConsecutiveFiresInTheZone()
        {
            var result = await ServiceAt(Now).PreviewScheduleRunsAsync(new PreviewScheduleRunsRequestDto
            {
                CronExpression = "0 9 * * *",
                TimeZone = "Asia/Dhaka",
                Count = 3,
            });

            result.IsSuccess.Should().BeTrue();
            result.Data!.OccurrencesUtc.Should().Equal(
                new DateTime(2026, 10, 27, 3, 0, 0, DateTimeKind.Utc),
                new DateTime(2026, 10, 28, 3, 0, 0, DateTimeKind.Utc),
                new DateTime(2026, 10, 29, 3, 0, 0, DateTimeKind.Utc));
        }

        [Fact]
        public async Task PreviewScheduleRuns_StopsAtTheEndOfTheWindow()
        {
            var result = await ServiceAt(Now).PreviewScheduleRunsAsync(new PreviewScheduleRunsRequestDto
            {
                CronExpression = "0 9 * * *",
                // Window Oct 31 10:10Z → Nov 2 10:10Z: the 09:00 fires on Nov 1 and Nov 2 only.
                StartDate = Now.AddDays(5),
                EndDate = Now.AddDays(7),
                Count = 5,
            });

            result.IsSuccess.Should().BeTrue();
            result.Data!.OccurrencesUtc.Should().Equal(new DateTime(2026, 11, 1, 9, 0, 0, DateTimeKind.Utc), new DateTime(2026, 11, 2, 9, 0, 0, DateTimeKind.Utc));

            var empty = await ServiceAt(Now).PreviewScheduleRunsAsync(new PreviewScheduleRunsRequestDto
            {
                CronExpression = "0 9 * * *",
                EndDate = Now.AddHours(1),
            });
            empty.IsSuccess.Should().BeTrue();
            empty.Data!.OccurrencesUtc.Should().BeEmpty();
        }

        public static TheoryData<string, string?, int, string> InvalidPreviews => new()
        {
            { "0 0 9 * * *", null, 3, "Cron expression is not valid" },
            { "not-a-cron", null, 3, "Cron expression is not valid" },
            { "", null, 3, "Cron expression is not valid" },
            { "0 9 * * * " + new string(' ', 100), null, 3, "Cron expression is not valid" },
            { "0 9 * * *", "Mars/Olympus", 3, "Time zone is not recognised" },
            { "0 9 * * *", null, 9, "Count must be between 1 and 5" },
            { "0 9 * * *", null, 0, "Count must be between 1 and 5" },
        };

        [Theory]
        [MemberData(nameof(InvalidPreviews))]
        public async Task PreviewScheduleRuns_InvalidInput_IsValidationFailed(string cron, string? zone, int count, string message)
        {
            var result = await _service.PreviewScheduleRunsAsync(new PreviewScheduleRunsRequestDto
            {
                CronExpression = cron,
                TimeZone = zone,
                Count = count,
            });

            result.IsSuccess.Should().BeFalse();
            result.Errors.Should().Equal(new Dictionary<string, string> { ["validation_failed"] = message });
        }
    }
}
