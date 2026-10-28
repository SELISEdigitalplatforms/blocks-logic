using Blocks.Genesis;
using DomainService.Notification.Push;
using Microsoft.AspNetCore.Mvc;

namespace Api.Controllers
{
    /// <summary>
    /// Push runtime state for the os settings screen, routed as <c>/api/Notifier/push-status</c>: devices per platform
    /// and the last test of each. The push configuration itself is owned and served by blocks-os.
    /// </summary>
    [ApiController]
    [Route("Notifier/push-status")]
    public sealed class NotifierPushStatusController : ControllerBase
    {
        private readonly IPushStatusService _status;

        /// <summary>Takes the push status service.</summary>
        public NotifierPushStatusController(IPushStatusService status)
        {
            _status = status;
        }

        /// <summary><c>GET</c> — device counts per platform (tenant DB) and the last test per platform.</summary>
        [HttpGet]
        [ProtectedEndPoint("blocks-os::notification::gets")]
        public Task<PushStatusView> GetPushStatus() => _status.GetAsync(HttpContext.RequestAborted);
    }
}
