using System.Reflection;
using Blocks.Genesis;
using BlocksTemplate.Api.Controllers;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;

namespace XUnitTest.Controllers
{
    /// <summary>
    /// Actions that have a protected twin in os/release carry the same named permission (owner decision 2026-10-14).
    /// Mail/GetMailBoxMails was anonymous; it now needs sign-in through its [ProtectedEndPoint].
    /// No plain [Authorize] next to a [ProtectedEndPoint] (owner decision 2026-10-14): the ProtectedEndPoint is itself
    /// an AuthorizeAttribute and its handler already rejects anonymous callers.
    /// Actions with no reusable twin get blocks-logic::<area>::read|manage (owner decision 2026-10-14).
    /// </summary>
    public class ProtectedTwinAuthorizationTests
    {
        private const string R = "blocks-release::";

        public static IEnumerable<object[]> Protected() => new[]
        {
            Row(typeof(CertificateController), "UploadCertificate", "blocks-os::project::mutate-token-validation-params"),
            Row(typeof(NotificationController), "Gets", "blocks-os::notification::gets"),
            Row(typeof(NotificationController), "Get", "blocks-os::notification::gets"),
            // Push settings: same permissions as the os notification configuration endpoints.
            Row(typeof(global::Api.Controllers.NotifierPushSettingsController), "GetPushSettings", "blocks-os::notification::gets"),
            Row(typeof(global::Api.Controllers.NotifierPushSettingsController), "SavePushSettings", "blocks-os::notification::save"),
            Row(typeof(global::Api.Controllers.NotifierPushSettingsController), "SendPushTest", "blocks-os::notification::save"),
            Row(typeof(global::Api.Controllers.ProjectController), "Gets", "blocks-os::project::projects"),
            Row(typeof(global::Api.Controllers.ProjectController), "Get", "blocks-os::project::projects"),
            Row(typeof(global::Api.Controllers.PeopleController), "Gets", "blocks-os::people::gets"),
            Row(typeof(global::Api.Controllers.ServiceController), "GetAll", "blocks-os::service::gets"),
            Row(typeof(MailController), "Gets", "blocks-os::mail::gets"),
            Row(typeof(SecretController), "GetAll", "blocks-os::secret::gets"),
            Row(typeof(DeploymentController), "IsAuthorized", R + "auth::authorization-status"),
            Row(typeof(DeploymentController), "AccessToken", R + "auth::access-token"),
            Row(typeof(DeploymentController), "RemoveAuthorization", R + "auth::remove-authorization"),
            Row(typeof(DeploymentController), "DeleteAuthorization", R + "auth::delete-authorization"),
            Row(typeof(DeploymentController), "GetReposList", R + "build::repos-list"),
            Row(typeof(DeploymentController), "GetUser", R + "github::user"),
            Row(typeof(DeploymentController), "GetRepos", R + "github::repos"),
            Row(typeof(DeploymentController), "GetBranches", R + "github::branches"),
            Row(typeof(DeploymentController), "GithubBranchExists", R + "github::branch-exists"),
            Row(typeof(DeploymentController), "UpdateRepoDomain", R + "build::repo-domain-update"),
        };

        private static object[] Row(Type t, string action, string resource) => new object[] { t, action, resource };

        [Theory]
        [MemberData(nameof(Protected))]
        public void ActionHasItsNamedPermission(Type controller, string action, string expected)
        {
            var method = controller.GetMethod(action, BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)!;
            method.GetCustomAttribute<ProtectedEndPointAttribute>()!.ResourceName.Should().Be(expected);
            method.GetCustomAttribute<AllowAnonymousAttribute>().Should().BeNull();
            HasPlainAuthorize(method).Should().BeFalse("[ProtectedEndPoint] already requires sign-in");
        }

        [Fact]
        public void MailBoxMailsNowRequiresSignIn()
        {
            // Sign-in comes from the ProtectedEndPoint (itself an AuthorizeAttribute); no plain [Authorize] beside it.
            var method = typeof(MailController).GetMethod(nameof(MailController.GetMailBoxMails))!;
            method.GetCustomAttribute<ProtectedEndPointAttribute>()!.ResourceName.Should().Be("blocks-logic::mail::read");
            method.GetCustomAttribute<AllowAnonymousAttribute>().Should().BeNull();
            HasPlainAuthorize(method).Should().BeFalse();
        }

        private static bool HasPlainAuthorize(MethodInfo method) =>
            method.GetCustomAttributes<AuthorizeAttribute>().Any(a => a.GetType() == typeof(AuthorizeAttribute));

        // New blocks-logic permissions (owner decision 2026-10-14): actions with only [Authorize] and no reusable twin.
        // GET / read-only = ::read, create / update / delete / send / run = ::manage. No plain [Authorize] beside it.
        private const string L = "blocks-logic::";

        public static IEnumerable<object[]> NewLogicPermissions() => new[]
        {
            Row(typeof(StorageController), "GetPreSignedUrlForUpload", L + "storage-file::manage"),
            Row(typeof(StorageController), "CompleteUpload", L + "storage-file::manage"),
            Row(typeof(StorageController), "GetFile", L + "storage-file::read"),
            Row(typeof(StorageController), "GetFiles", L + "storage-file::read"),
            Row(typeof(StorageController), "DeleteFile", L + "storage-file::manage"),
            Row(typeof(MailController), "SendToAny", L + "mail::send-any"),
            Row(typeof(MailController), "Send", L + "mail::manage"),
            Row(typeof(MailController), "GetMailBoxMails", L + "mail::read"),
            Row(typeof(MailController), "GetMailBoxMail", L + "mail::read"),
            Row(typeof(MonitorController), "GetMonitorList", L + "monitor::read"),
            Row(typeof(MonitorController), "GetMonitorListByRepoId", L + "monitor::read"),
            Row(typeof(MonitorController), "GetMonitorById", L + "monitor::read"),
            Row(typeof(MonitorController), "GetIncidentList", L + "monitor::read"),
            Row(typeof(MonitorController), "GetMonitorDetails", L + "monitor::read"),
            Row(typeof(MonitorController), "GetMonitorResponseTime", L + "monitor::read"),
            Row(typeof(MonitorController), "GetMonitorDownTime", L + "monitor::read"),
            Row(typeof(MonitorController), "IsExternalServiceConfigured", L + "monitor::read"),
            Row(typeof(HealthController), "SaveHealth", L + "health::manage"),
            Row(typeof(HealthController), "UpdateHealth", L + "health::manage"),
            Row(typeof(HealthController), "DeleteHealth", L + "health::manage"),
            Row(typeof(LanguageController), "Gets", L + "language::read"),
            Row(typeof(global::Api.Controllers.TemplateController), "Gets", L + "template::read"),
            Row(typeof(global::Api.Controllers.NotifierController), "GetUnreadNotificationsBySubscriptionFilter", L + "notifier::read"),
            Row(typeof(global::Api.Controllers.NotifierController), "MarkAllNotificationAsRead", L + "notifier::manage"),
            Row(typeof(global::Api.Controllers.NotifierController), "MarkNotificationAsRead", L + "notifier::manage"),
            Row(typeof(global::Api.Controllers.NotifierController), "GetNotifications", L + "notifier::read"),
            Row(typeof(global::Api.Controllers.NotifierDevicesController), "RegisterDevice", L + "notifier::manage"),
            Row(typeof(global::Api.Controllers.NotifierDevicesController), "RemoveDevice", L + "notifier::manage"),
            Row(typeof(SchedulerController), "CreateSchedule", L + "scheduler::manage"),
            Row(typeof(SchedulerController), "UpdateSchedule", L + "scheduler::manage"),
            Row(typeof(SchedulerController), "DeleteSchedule", L + "scheduler::manage"),
            Row(typeof(SchedulerController), "GetSchedules", L + "scheduler::read"),
            Row(typeof(global::Utilities.Api.Controllers.WorkflowController), "GetAll", L + "workflow::read"),
            Row(typeof(global::Utilities.Api.Controllers.WorkflowController), "Get", L + "workflow::read"),
            Row(typeof(global::Utilities.Api.Controllers.WorkflowController), "Create", L + "workflow::manage"),
            Row(typeof(global::Utilities.Api.Controllers.WorkflowController), "Duplicate", L + "workflow::manage"),
            Row(typeof(global::Utilities.Api.Controllers.WorkflowController), "Update", L + "workflow::manage"),
            Row(typeof(global::Utilities.Api.Controllers.WorkflowController), "Import", L + "workflow::manage"),
            Row(typeof(global::Utilities.Api.Controllers.WorkflowController), "Delete", L + "workflow::manage"),
            Row(typeof(global::Utilities.Api.Controllers.WorkflowController), "CreateVersion", L + "workflow::manage"),
            Row(typeof(global::Utilities.Api.Controllers.WorkflowController), "UpdateVersion", L + "workflow::manage"),
            Row(typeof(global::Utilities.Api.Controllers.WorkflowController), "GetVersions", L + "workflow::read"),
            Row(typeof(global::Utilities.Api.Controllers.WorkflowController), "GetWorkflowByVersion", L + "workflow::read"),
            Row(typeof(global::Utilities.Api.Controllers.WorkflowController), "PublishNewVersion", L + "workflow::manage"),
            Row(typeof(global::Utilities.Api.Controllers.WorkflowController), "PublishVersion", L + "workflow::manage"),
            Row(typeof(global::Utilities.Api.Controllers.WorkflowController), "Unpublish", L + "workflow::manage"),
            Row(typeof(global::Utilities.Api.Controllers.WorkflowController), "Restore", L + "workflow::manage"),
            Row(typeof(global::Utilities.Api.Controllers.WorkflowController), "StepExecute", L + "workflow::manage"),
            Row(typeof(global::Utilities.Api.Controllers.WorkflowController), "TriggerListener", L + "workflow::manage"),
            Row(typeof(global::Utilities.Api.Controllers.WorkflowController), "GetExecutions", L + "workflow::read"),
            Row(typeof(global::Utilities.Api.Controllers.WorkflowController), "GetExecution", L + "workflow::read"),
            Row(typeof(global::Utilities.Api.Controllers.WorkflowController), "ResumeExecution", L + "workflow::manage"),
            Row(typeof(global::Utilities.Api.Controllers.WorkflowController), "GetExecutionLogs", L + "workflow::read"),
            Row(typeof(global::Utilities.Api.Controllers.WorkflowController), "LastSuccessfullExecution", L + "workflow::read"),
        };

        [Theory]
        [MemberData(nameof(NewLogicPermissions))]
        public void ActionHasItsNewLogicPermission(Type controller, string action, string expected)
        {
            var method = controller.GetMethod(action, BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)!;
            method.GetCustomAttribute<ProtectedEndPointAttribute>()!.ResourceName.Should().Be(expected);
            method.GetCustomAttribute<AllowAnonymousAttribute>().Should().BeNull();
            // Owner decision 2026-10-14: no plain [Authorize] next to the ProtectedEndPoint (itself an AuthorizeAttribute).
            HasPlainAuthorize(method).Should().BeFalse();
        }

        [Fact]
        public void NewLogicPermissionListIsComplete()
        {
            NewLogicPermissions().Should().HaveCount(54);
            NewLogicPermissions().Select(r => (string)r[2]).Distinct().Should().HaveCount(15, "mail::send-any was split from mail::manage, 2026-10-28");
        }

        // Open by design (data plane with its own check, or a public callback): no named permission, no [Authorize].
        public static IEnumerable<object[]> OpenByDesign() => new[]
        {
            Row(typeof(HealthController), "Ping", ""),
            Row(typeof(global::Api.Controllers.NotifierController), "Notify", ""),
            Row(typeof(global::Api.Controllers.NotifierController), "SendSecretNotification", ""),
            Row(typeof(global::Utilities.Api.Controllers.WorkflowController), "Webhook", ""),
            Row(typeof(global::Utilities.Api.Controllers.WorkflowController), "TestWebhook", ""),
            Row(typeof(global::Utilities.Api.Controllers.WorkflowController), "WebhookByHeader", ""),
            Row(typeof(global::Utilities.Api.Controllers.WorkflowController), "TestWebhookByHeader", ""),
            Row(typeof(global::MailBoxSyncService.Controllers.SesEventsController), "Receive", ""),
            Row(typeof(FunctionsController), "Invoke", ""),
            Row(typeof(FunctionsController), "PollRunResult", ""),
            Row(typeof(global::Utilities.Api.Controllers.ProxiesController), "Gateway", ""),
        };

        [Theory]
        [MemberData(nameof(OpenByDesign))]
        public void OpenByDesignRoutesStayOpen(Type controller, string action, string _)
        {
            var method = controller.GetMethod(action, BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)!;
            method.Should().NotBeNull();
            method.GetCustomAttribute<ProtectedEndPointAttribute>().Should().BeNull();
            method.GetCustomAttributes<AuthorizeAttribute>().Should().BeEmpty();
        }
    }
}
