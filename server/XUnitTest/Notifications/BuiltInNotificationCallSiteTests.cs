using Blocks.Genesis;
using Blocks.Genesis.Entities.Logic;
using DataAccess.InternalService.Notifications;
using FluentAssertions;
using Functions.DomainService.Consumers;
using Functions.DomainService.Queue;
using Functions.DomainService.Repositories;
using Functions.DomainService.Services;
using Mail.DomainService.Shared.Enums;
using IMailRepository = Mail.DomainService.Services.IMailRepository;
using Mail.DomainService.Dtos;
using Mail.DomainService.Entities;
using Mail.DomainService.Mails;
using Mail.DomainService.Mails.Strategies;
using Mail.DomainService.Services;
using Mail.DomainService.Utilities;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Scheduler.DomainService.Dtos.Requests;
using Scheduler.DomainService.Models;
using Scheduler.DomainService.Repositories;
using Scheduler.DomainService.Services;
using Workflow.DomainService.Entities;
using Workflow.DomainService.Logging;
using Workflow.DomainService.Nodes.TriggerScheduleV1;
using Workflow.DomainService.Repositories;
using Workflow.DomainService.Services;
using XUnitTest.TestHelpers;
using EmailTemplate = Blocks.Genesis.Entities.Os.EmailTemplate;

namespace XUnitTest.Notifications
{
    /// <summary>
    /// Where each built-in notification is sent: the right config name, recipient and tenant, nothing sent when the
    /// action did not happen, and a failing sender never breaks the action.
    /// </summary>
    public class BuiltInNotificationCallSiteTests : IDisposable
    {
        private const string Tenant = "tenant-1";

        private readonly Mock<IBuiltInNotificationSender> _sender = new();

        public BuiltInNotificationCallSiteTests() => TestBlocksContext.Set(Tenant, "acting-user");

        public void Dispose() => TestBlocksContext.Clear();

        private void SenderThrows()
        {
            _sender.Setup(s => s.SendAsync(
                    It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IReadOnlyCollection<string>?>(),
                    It.IsAny<BuiltInNotificationPayload>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("boom"));
            _sender.Setup(s => s.SendToTenantAdminsAsync(
                    It.IsAny<string>(), It.IsAny<string>(), It.IsAny<BuiltInNotificationPayload>(),
                    It.IsAny<string?>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("boom"));
        }

        private void VerifyUsers(string tenant, string config, string owner, Times times, Func<BuiltInNotificationPayload, bool>? payload = null) =>
            _sender.Verify(s => s.SendAsync(
                tenant, config,
                It.Is<IReadOnlyCollection<string>?>(u => u != null && u.Count == 1 && u.Contains(owner)),
                It.Is<BuiltInNotificationPayload>(p => payload == null || payload(p)),
                It.IsAny<string?>(), It.IsAny<CancellationToken>()), times);

        private void VerifyNothingSent()
        {
            _sender.Verify(s => s.SendAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IReadOnlyCollection<string>?>(),
                It.IsAny<BuiltInNotificationPayload>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
            _sender.Verify(s => s.SendToTenantAdminsAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<BuiltInNotificationPayload>(),
                It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        // ================================ workflow run failed (H1, C5) ================================

        private WorkflowNotificationService WorkflowNotifier()
        {
            return new WorkflowNotificationService(
                new ConfigurationBuilder().Build(), Mock.Of<Blocks.Genesis.Notifications.INotificationPublisher>(),
                NullLogger<WorkflowNotificationService>.Instance, _sender.Object);
        }

        private static WorkflowExecutionEntity Execution(WorkflowExecutionMode mode, List<string>? testUsers = null) => new()
        {
            Id = "exec-1",
            TenantId = Tenant,
            WorkflowId = "wf-1",
            WorkflowName = "Flow",
            ExecutionMode = mode,
            TriggerMetadata = new TriggerMetadata(),
            WorkflowSnapshot = new WorkflowEntity
            {
                ItemId = "wf-1",
                TenantId = Tenant,
                Name = "Flow",
                CreatedBy = "owner-1",
                TestMeta = testUsers is null ? null : new TestWorkflowMeta { UserIds = testUsers },
            },
        };

        private static Task Fire(WorkflowNotificationService sut, WorkflowExecutionEntity run, string eventName) =>
            sut.NotifyExecutionEventAsync(run, null, eventName, "code", "Failed", run.Id, "message");

        [Fact]
        public async Task A_production_run_that_failed_tells_the_workflow_owner_in_the_run_tenant()
        {
            await Fire(WorkflowNotifier(), Execution(WorkflowExecutionMode.Production), "WorkflowFailed");

            VerifyUsers(Tenant, BuiltInNotificationNames.WorkflowRunFailed, "owner-1", Times.Once(),
                p => p.IsSuccess == false && p.Ids!["workflowId"] == "wf-1" && p.Ids["executionId"] == "exec-1");
        }

        [Fact]
        public async Task A_test_run_that_failed_is_not_sent_as_a_built_in_alert()
        {
            await Fire(WorkflowNotifier(), Execution(WorkflowExecutionMode.Test, ["tester-1"]), "WorkflowFailed");

            VerifyNothingSent();
        }

        [Theory]
        [InlineData("WorkflowCompleted")]
        [InlineData("NodeFailed")]
        [InlineData("WorkflowStarted")]
        public async Task Other_events_of_a_production_run_are_not_sent(string eventName)
        {
            await Fire(WorkflowNotifier(), Execution(WorkflowExecutionMode.Production), eventName);

            VerifyNothingSent();
        }

        [Fact]
        public async Task A_sender_that_throws_does_not_break_the_run_event()
        {
            SenderThrows();

            var act = () => Fire(WorkflowNotifier(), Execution(WorkflowExecutionMode.Production), "WorkflowFailed");

            await act.Should().NotThrowAsync();
        }

        // ================================ scheduled trigger dropped (H2) ================================

        private (WorkflowExecutionService Sut, Mock<IWorkflowRepository> Workflows, Mock<IWorkflowNotificationService> Notifier, Mock<IWorkflowVersionRepository> Versions) ExecutionService()
        {
            var workflows = new Mock<IWorkflowRepository>();
            var notifier = new Mock<IWorkflowNotificationService>();
            var versions = new Mock<IWorkflowVersionRepository>();
            var sut = new WorkflowExecutionService(
                workflows.Object, Mock.Of<IWorkflowExecutionRepository>(), Mock.Of<IMessageClient>(),
                Mock.Of<ILogger<WorkflowExecutionService>>(), Mock.Of<IWorkflowEngineService>(), versions.Object,
                notifier.Object, Mock.Of<IWorkflowAuthService>(), Mock.Of<IHttpContextAccessor>(),
                Mock.Of<IDelegationGrantFactory>(), new WorkflowExecutionLogger(NullLogger<WorkflowExecutionLogger>.Instance),
                Mock.Of<IInProcessGrantReleaser>(),
                Mock.Of<global::Workflow.DomainService.BlocksEvents.IBlocksEventCatalogService>());
            return (sut, workflows, notifier, versions);
        }

        private static SchedulerTriggerPayload Trigger() => new() { WorkflowId = "wf-1", TriggerId = "node-1", TenantId = Tenant, CronExpression = "* * * * *" };

        [Fact]
        public async Task A_trigger_for_an_unpublished_workflow_is_dropped_and_the_owner_is_told()
        {
            var (sut, workflows, notifier, _) = ExecutionService();
            var workflow = new WorkflowEntity { ItemId = "wf-1", TenantId = Tenant, CreatedBy = "owner-1", IsPublished = false };
            workflows.Setup(w => w.GetWorkflowAsync(Tenant, "wf-1")).ReturnsAsync(workflow);

            await sut.SchedulerTriggerStartAsync(Trigger());

            notifier.Verify(n => n.NotifyScheduledTriggerDroppedAsync(Tenant, workflow, "not-published"), Times.Once);
        }

        [Fact]
        public async Task A_missing_published_snapshot_tells_the_owner()
        {
            var (sut, workflows, notifier, versions) = ExecutionService();
            var workflow = new WorkflowEntity { ItemId = "wf-1", TenantId = Tenant, CreatedBy = "owner-1", IsPublished = true, PublishedVersionId = "v-1" };
            workflows.Setup(w => w.GetWorkflowAsync(Tenant, "wf-1")).ReturnsAsync(workflow);
            versions.Setup(v => v.GetWorkflowVersionAsync(Tenant, "v-1")).ReturnsAsync((WorkflowVersionEntity)null!);

            await sut.SchedulerTriggerStartAsync(Trigger());

            notifier.Verify(n => n.NotifyScheduledTriggerDroppedAsync(Tenant, workflow, "version-missing"), Times.Once);
        }

        [Fact]
        public async Task A_missing_workflow_has_no_owner_so_nobody_is_told()
        {
            var (sut, workflows, notifier, _) = ExecutionService();
            workflows.Setup(w => w.GetWorkflowAsync(Tenant, "wf-1")).ReturnsAsync((WorkflowEntity)null!);

            await sut.SchedulerTriggerStartAsync(Trigger());

            notifier.Verify(n => n.NotifyScheduledTriggerDroppedAsync(It.IsAny<string>(), It.IsAny<WorkflowEntity>(), It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task The_dropped_trigger_message_goes_to_the_owner_with_ids_only()
        {
            var workflow = new WorkflowEntity { ItemId = "wf-1", TenantId = Tenant, CreatedBy = "owner-1" };

            await WorkflowNotifier().NotifyScheduledTriggerDroppedAsync(Tenant, workflow, "not-published");

            VerifyUsers(Tenant, BuiltInNotificationNames.ScheduledTriggerDropped, "owner-1", Times.Once(),
                p => p.Ids!["workflowId"] == "wf-1" && p.Ids["reason"] == "not-published");
        }

        [Fact]
        public async Task A_sender_that_throws_does_not_break_the_dropped_trigger_path()
        {
            SenderThrows();

            var act = () => WorkflowNotifier().NotifyScheduledTriggerDroppedAsync(
                Tenant, new WorkflowEntity { ItemId = "wf-1", TenantId = Tenant, CreatedBy = "owner-1" }, "not-published");

            await act.Should().NotThrowAsync();
        }

        // ================================ function build finished (H3, C7) ================================

        private (FunctionBuildResultConsumer Consumer, Mock<IFunctionBuildRepository> Builds) BuildConsumer(bool applied = true)
        {
            var builds = new Mock<IFunctionBuildRepository>();
            builds.Setup(b => b.ApplyResultAsync(
                    Tenant, "build-1", It.IsAny<BuildStatus>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(),
                    It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(applied);
            builds.Setup(b => b.GetByIdAsync(Tenant, "build-1", It.IsAny<CancellationToken>()))
                .ReturnsAsync(new FunctionBuildEntity { ItemId = "build-1", FunctionId = "fn-1" });
            var functions = new Mock<IFunctionRepository>();
            functions.Setup(f => f.GetByIdAsync(Tenant, "fn-1", It.IsAny<CancellationToken>()))
                .ReturnsAsync(new FunctionEntity { ItemId = "fn-1", CreatedBy = "author-1" });
            var cache = new Mock<ICacheClient>();
            cache.Setup(c => c.CacheDatabase()).Returns(FakeRedis());
            return (new FunctionBuildResultConsumer(
                cache.Object, builds.Object, NullLogger<FunctionBuildResultConsumer>.Instance, functions.Object, _sender.Object), builds);
        }

        private static StackExchange.Redis.IDatabase FakeRedis() => XUnitTest.Functions.FakeRedisDatabase.Create().Database;

        private static ResultStreamEntry BuildEntry(string status) =>
            new("1-0", new Dictionary<string, string>(StringComparer.Ordinal) { ["buildId"] = "build-1", ["tenantId"] = Tenant, ["status"] = status });

        [Theory]
        [InlineData("SUCCEEDED", true)]
        [InlineData("FAILED", false)]
        public async Task An_applied_build_result_tells_the_author(string status, bool succeeded)
        {
            var (consumer, _) = BuildConsumer();

            await consumer.ProcessAsync(BuildEntry(status), CancellationToken.None);

            VerifyUsers(Tenant, BuiltInNotificationNames.FunctionBuildFinished, "author-1", Times.Once(),
                p => p.IsSuccess == succeeded && p.Ids!["buildId"] == "build-1" && p.Ids["functionId"] == "fn-1");
        }

        [Fact]
        public async Task A_build_result_that_matched_no_record_tells_nobody()
        {
            var (consumer, _) = BuildConsumer(applied: false);

            await consumer.ProcessAsync(BuildEntry("FAILED"), CancellationToken.None);

            VerifyNothingSent();
        }

        [Fact]
        public async Task A_sender_that_throws_does_not_fail_the_build_result()
        {
            SenderThrows();
            var (consumer, _) = BuildConsumer();

            var act = () => consumer.ProcessAsync(BuildEntry("FAILED"), CancellationToken.None);

            await act.Should().NotThrowAsync();
        }

        // ================================ function changed by other (H11, C6, C9) ================================

        private FunctionOwnerNotifier OwnerNotifier() => new(Mock.Of<IFunctionRepository>(), _sender.Object);

        private static FunctionEntity Function(string? owner) => new() { ItemId = "fn-1", CreatedBy = owner };

        [Theory]
        [InlineData("exported")]
        [InlineData("imported")]
        [InlineData("deleted")]
        public async Task Another_user_changing_a_function_tells_the_owner(string action)
        {
            await OwnerNotifier().ChangedByOtherAsync(Tenant, Function("owner-1"), "other-user", action);

            VerifyUsers(Tenant, BuiltInNotificationNames.FunctionChangedByOther, "owner-1", Times.Once(),
                p => p.Ids!["action"] == action && p.Ids["functionId"] == "fn-1");
        }

        [Theory]
        [InlineData("owner-1")]
        [InlineData(null)]
        [InlineData("")]
        public async Task The_owner_or_an_unknown_actor_does_not_trigger_a_notice(string? actor)
        {
            await OwnerNotifier().ChangedByOtherAsync(Tenant, Function("owner-1"), actor, "exported");

            VerifyNothingSent();
        }

        [Fact]
        public async Task A_function_without_an_owner_is_sent_with_no_recipient_and_does_not_throw()
        {
            await OwnerNotifier().ChangedByOtherAsync(Tenant, Function(null), "other-user", "exported");

            _sender.Verify(s => s.SendAsync(
                Tenant, BuiltInNotificationNames.FunctionChangedByOther, null,
                It.IsAny<BuiltInNotificationPayload>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task A_sender_that_throws_does_not_break_the_function_action()
        {
            SenderThrows();

            var act = () => OwnerNotifier().ChangedByOtherAsync(Tenant, Function("owner-1"), "other-user", "deleted");

            await act.Should().NotThrowAsync();
        }

        [Fact]
        public async Task Function_notices_carry_ids_and_fixed_text_only()
        {
            BuiltInNotificationPayload? seen = null;
            _sender.Setup(s => s.SendAsync(
                    It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IReadOnlyCollection<string>?>(),
                    It.IsAny<BuiltInNotificationPayload>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
                .Callback((string _, string _, IReadOnlyCollection<string>? _, BuiltInNotificationPayload p, string? _, CancellationToken _) => seen = p)
                .ReturnsAsync(true);

            await OwnerNotifier().ChangedByOtherAsync(Tenant, Function("owner-1"), "other-user-secret@example.com", "exported");

            seen.Should().NotBeNull();
            seen!.Ids!.Keys.Should().BeEquivalentTo("functionId", "action");
            (seen.Title + seen.Description + string.Join("", seen.Ids.Values)).Should().NotContain("other-user-secret");
        }

        // ================================ mail send failed (H7, C4, C10) ================================

        private SendMailService MailService(IOutboundMailSender? sender = null, bool withSender = true)
        {
            var registry = new OutboundMailSenderRegistry(sender is null ? [] : [sender]);
            var resolver = new Mock<IMailAttachmentResolver>();
            resolver.Setup(r => r.ResolveAsync(It.IsAny<IEnumerable<string>?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<MailAttachment>());
            return new SendMailService(
                NullLogger<SendMailService>.Instance, Mock.Of<IMailRepository>(), registry,
                new MailSendResolvers(resolver.Object, MailSecretsTestDoubles.PasswordResolver()), Mock.Of<IMessageClient>(),
                Options.Create(new MailStatusEventOptions { Enabled = false }),
                withSender ? _sender.Object : null);
        }

        private static MailToBeSent Mail(bool test = false, bool complete = true) => new()
        {
            ItemId = "mail-1",
            Name = "welcome",
            Language = "en-US",
            To = ["private.person@example.com"],
            Attachments = [],
            BodyDataContext = new Dictionary<string, string> { ["Secret"] = "do-not-leak" },
            SubjectDataContext = new Dictionary<string, string>(),
            IsTestMail = test,
            EmailTemplate = complete ? new EmailTemplate { Name = "welcome", TemplateSubject = "s", TemplateBody = "b" } : null!,
            MailServerConfiguration = complete ? new MailServerConfiguration { Provider = MailServiceProvider.Zoho, Host = "h" } : null!,
        };

        [Fact]
        public async Task A_mail_with_no_template_tells_the_tenant_admins_and_returns_false_as_before()
        {
            var ok = await MailService().ProcessSendMailAsync(Mail(complete: false));

            ok.Should().BeFalse();
            _sender.Verify(s => s.SendToTenantAdminsAsync(
                Tenant, BuiltInNotificationNames.MailSendFailed,
                It.Is<BuiltInNotificationPayload>(p => p.IsSuccess == false && p.Ids!["mailId"] == "mail-1" && p.Ids["purpose"] == "welcome"),
                "welcome", It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task A_mail_with_an_unregistered_provider_tells_the_admins()
        {
            var ok = await MailService().ProcessSendMailAsync(Mail());

            ok.Should().BeFalse();
            _sender.Verify(s => s.SendToTenantAdminsAsync(
                Tenant, BuiltInNotificationNames.MailSendFailed, It.IsAny<BuiltInNotificationPayload>(),
                It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task A_mail_that_the_smtp_server_refused_tells_the_admins()
        {
            var smtp = new Mock<IOutboundMailSender>();
            smtp.SetupGet(s => s.Provider).Returns(MailServiceProvider.Zoho);
            smtp.Setup(s => s.SendAsync(It.IsAny<MailToBeSent>(), It.IsAny<MailBody>())).ReturnsAsync(false);

            var ok = await MailService(smtp.Object).ProcessSendMailAsync(Mail());

            ok.Should().BeFalse();
            _sender.Verify(s => s.SendToTenantAdminsAsync(
                Tenant, BuiltInNotificationNames.MailSendFailed, It.IsAny<BuiltInNotificationPayload>(),
                It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task A_mail_that_was_sent_tells_nobody()
        {
            var smtp = new Mock<IOutboundMailSender>();
            smtp.SetupGet(s => s.Provider).Returns(MailServiceProvider.Zoho);
            smtp.Setup(s => s.SendAsync(It.IsAny<MailToBeSent>(), It.IsAny<MailBody>())).ReturnsAsync(true);

            var ok = await MailService(smtp.Object).ProcessSendMailAsync(Mail());

            ok.Should().BeTrue();
            VerifyNothingSent();
        }

        [Fact]
        public async Task A_failed_test_mail_is_not_reported()
        {
            await MailService().ProcessSendMailAsync(Mail(test: true, complete: false));

            VerifyNothingSent();
        }

        [Fact]
        public async Task The_mail_notice_carries_no_address_body_or_data_value()
        {
            BuiltInNotificationPayload? seen = null;
            _sender.Setup(s => s.SendToTenantAdminsAsync(
                    It.IsAny<string>(), It.IsAny<string>(), It.IsAny<BuiltInNotificationPayload>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
                .Callback((string _, string _, BuiltInNotificationPayload p, string? _, CancellationToken _) => seen = p)
                .ReturnsAsync(true);

            await MailService().ProcessSendMailAsync(Mail(complete: false));

            var text = seen!.Title + seen.Description + string.Join("", seen.Ids!.Values);
            text.Should().NotContain("private.person").And.NotContain("do-not-leak");
        }

        [Fact]
        public async Task A_failing_notifier_never_changes_the_mail_result_and_never_sends_a_mail()
        {
            SenderThrows();
            var messages = new Mock<IMessageClient>();
            var service = new SendMailService(
                NullLogger<SendMailService>.Instance, Mock.Of<IMailRepository>(), new OutboundMailSenderRegistry([]),
                new MailSendResolvers(Mock.Of<IMailAttachmentResolver>(), MailSecretsTestDoubles.PasswordResolver()), messages.Object,
                Options.Create(new MailStatusEventOptions { Enabled = false }), _sender.Object);

            var ok = await service.ProcessSendMailAsync(Mail(complete: false));

            ok.Should().BeFalse();
            messages.VerifyNoOtherCalls(); // no queue message: the failure path never re-enters the mail path
        }

        [Fact]
        public async Task Without_a_sender_the_mail_path_is_unchanged()
        {
            var ok = await MailService(withSender: false).ProcessSendMailAsync(Mail(complete: false));

            ok.Should().BeFalse();
        }

        // ================================ schedule registration failed (H9, H10) ================================

        [Fact]
        public async Task A_workflow_schedule_that_could_not_be_enqueued_tells_the_publisher()
        {
            var repo = new Mock<IScheduleRepository>();
            var messages = new Mock<IMessageClient>();
            messages.Setup(m => m.SendToConsumerAsync(It.IsAny<ConsumerMessage<global::Scheduler.DomainService.Events.ScheduleJobUpsertedEvent>>()))
                .ThrowsAsync(new InvalidOperationException("queue down"));
            var service = new ScheduleService(repo.Object, messages.Object, NullLogger<ScheduleService>.Instance, _sender.Object);

            var result = await service.CreateWorkflowScheduleAsync(new CreateWorkflowScheduleRequest
            {
                WorkflowId = "wf-1",
                NodeId = "node-1",
                TenantId = Tenant,
                CronExpression = "30 */10 * * * *",
            });

            result.IsSuccess.Should().BeFalse();
            VerifyUsers(Tenant, BuiltInNotificationNames.ScheduleRegistrationFailed, "acting-user", Times.Once(),
                p => p.Ids!["workflowId"] == "wf-1" && p.Ids["scheduleId"] != null);
        }

        [Fact]
        public async Task A_workflow_schedule_that_was_enqueued_tells_nobody()
        {
            var service = new ScheduleService(
                Mock.Of<IScheduleRepository>(), Mock.Of<IMessageClient>(), NullLogger<ScheduleService>.Instance, _sender.Object);

            var result = await service.CreateWorkflowScheduleAsync(new CreateWorkflowScheduleRequest
            {
                WorkflowId = "wf-1",
                NodeId = "node-1",
                TenantId = Tenant,
                CronExpression = "30 */10 * * * *",
            });

            result.IsSuccess.Should().BeTrue();
            VerifyNothingSent();
        }

        [Fact]
        public async Task A_failed_save_of_a_workflow_schedule_tells_nobody_because_nothing_was_stored()
        {
            var repo = new Mock<IScheduleRepository>();
            repo.Setup(r => r.CreateAsync(It.IsAny<Schedule>())).ThrowsAsync(new InvalidOperationException("db down"));
            var service = new ScheduleService(repo.Object, Mock.Of<IMessageClient>(), NullLogger<ScheduleService>.Instance, _sender.Object);

            var result = await service.CreateWorkflowScheduleAsync(new CreateWorkflowScheduleRequest
            {
                WorkflowId = "wf-1",
                NodeId = "node-1",
                TenantId = Tenant,
                CronExpression = "30 */10 * * * *",
            });

            result.IsSuccess.Should().BeFalse();
            VerifyNothingSent();
        }

        [Fact]
        public async Task A_sender_that_throws_does_not_change_the_enqueue_error_answer()
        {
            SenderThrows();
            var messages = new Mock<IMessageClient>();
            messages.Setup(m => m.SendToConsumerAsync(It.IsAny<ConsumerMessage<global::Scheduler.DomainService.Events.ScheduleJobUpsertedEvent>>()))
                .ThrowsAsync(new InvalidOperationException("queue down"));
            var service = new ScheduleService(Mock.Of<IScheduleRepository>(), messages.Object, NullLogger<ScheduleService>.Instance, _sender.Object);

            var result = await service.CreateWorkflowScheduleAsync(new CreateWorkflowScheduleRequest
            {
                WorkflowId = "wf-1",
                NodeId = "node-1",
                TenantId = Tenant,
                CronExpression = "30 */10 * * * *",
            });

            result.IsSuccess.Should().BeFalse();
            result.Errors.Should().ContainKey("enqueue_failed");
        }

        [Fact]
        public async Task Registration_notices_skip_a_missing_tenant_or_owner()
        {
            await ScheduleRegistrationNotifier.NotifyAsync(_sender.Object, "", "owner-1", "s-1", null);
            await ScheduleRegistrationNotifier.NotifyAsync(null, Tenant, "owner-1", "s-1", null);

            VerifyNothingSent();

            await ScheduleRegistrationNotifier.NotifyAsync(_sender.Object, Tenant, null, "s-1", null);
            _sender.Verify(s => s.SendAsync(
                Tenant, BuiltInNotificationNames.ScheduleRegistrationFailed, null,
                It.IsAny<BuiltInNotificationPayload>(), "s-1", It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task A_sender_that_throws_does_not_break_registration_notices()
        {
            SenderThrows();

            var act = () => ScheduleRegistrationNotifier.NotifyAsync(_sender.Object, Tenant, "owner-1", "s-1", "wf-1");

            await act.Should().NotThrowAsync();
        }
    }
}
