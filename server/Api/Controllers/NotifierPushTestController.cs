using Blocks.Genesis;
using DomainService.Notification.Push;
using Microsoft.AspNetCore.Mvc;

namespace Api.Controllers
{
    /// <summary>
    /// Test pushes to the caller's own devices, routed as <c>/api/Notifier/push-test</c>. A refused test answers 200
    /// with <c>{ isSuccess: false, errors }</c>; a sent test lists the result per device.
    /// </summary>
    [ApiController]
    [Route("Notifier/push-test")]
    public sealed class NotifierPushTestController : ControllerBase
    {
        private const string SavePermission = "blocks-os::notification::save";

        private readonly IPushTestService _tests;

        /// <summary>Takes the push test service.</summary>
        public NotifierPushTestController(IPushTestService tests)
        {
            _tests = tests;
        }

        /// <summary><c>POST</c> — a fixed test text to one platform, or to every platform that is on.</summary>
        [HttpPost]
        [ProtectedEndPoint(SavePermission)]
        public Task<PushTestResult> SendPushTest([FromBody] PushTestRequest request) =>
            _tests.SendTestAsync(request, HttpContext.RequestAborted);

        /// <summary><c>POST notification</c> — an unsaved notification draft with sample data.</summary>
        [HttpPost("notification")]
        [ProtectedEndPoint(SavePermission)]
        public Task<PushTestResult> SendNotificationPushTest([FromBody] PushNotificationTestRequest request) =>
            _tests.SendNotificationTestAsync(request, HttpContext.RequestAborted);
    }
}
