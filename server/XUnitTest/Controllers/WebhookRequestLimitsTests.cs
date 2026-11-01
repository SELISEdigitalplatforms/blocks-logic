using System.Reflection;
using BlocksTemplate.Api.Controllers;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Utilities.Api.Controllers;
using Workflow.DomainService.Services;

namespace XUnitTest.Controllers
{
    /// <summary>
    /// SPEC8 limits on the routes: every webhook action allows a 50 MiB file request and binds no body itself, and
    /// the certificate upload keeps its 15 MB form limit now that the global multipart limit is 50 MiB (C17).
    /// The end-to-end 413 behaviour behind a real server is covered by the E2E steps.
    /// </summary>
    public class WebhookRequestLimitsTests
    {
        public static TheoryData<string> WebhookActions => new()
        {
            nameof(WorkflowController.Webhook),
            nameof(WorkflowController.TestWebhook),
            nameof(WorkflowController.WebhookByHeader),
            nameof(WorkflowController.TestWebhookByHeader),
        };

        private static MethodInfo Action(string name) => typeof(WorkflowController).GetMethod(name)!;

        [Theory]
        [MemberData(nameof(WebhookActions))]
        public void Webhook_actions_allow_50_MiB_and_bind_no_body(string name)
        {
            var action = Action(name);

            action.GetCustomAttribute<RequestSizeLimitAttribute>().Should().NotBeNull();
            action.GetCustomAttribute<RequestFormLimitsAttribute>()!.MultipartBodyLengthLimit
                .Should().Be(WebhookIntakeLimits.FileRequestMaxBytes);
            action.GetParameters().Should().OnlyContain(p => p.ParameterType == typeof(string),
                "the body is read by the service after auth, never bound by the framework");
        }

        [Fact]
        public void Request_size_limit_leaves_room_above_50_MiB_for_multipart_framing()
        {
            WebhookIntakeLimits.RequestSizeLimitBytes.Should().BeGreaterThan(WebhookIntakeLimits.FileRequestMaxBytes);
        }

        [Fact]
        public void Certificate_upload_keeps_its_15_MB_form_limit()
        {
            typeof(CertificateController).GetMethod(nameof(CertificateController.UploadCertificate))!
                .GetCustomAttribute<RequestFormLimitsAttribute>()!.MultipartBodyLengthLimit
                .Should().Be(15 * 1024 * 1024);
        }
    }
}
