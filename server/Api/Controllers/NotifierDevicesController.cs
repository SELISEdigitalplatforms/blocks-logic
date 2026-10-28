using Blocks.Genesis;
using DomainService.Notification.Push;
using Microsoft.AspNetCore.Mvc;

namespace Api.Controllers
{
    /// <summary>
    /// The caller's own mobile push devices, routed as <c>/api/Notifier/devices</c> (route fixed by the clients). Like the
    /// other notifier actions, a refused request answers 200 with <c>{ isSuccess: false, errors }</c>.
    /// </summary>
    [ApiController]
    [Route("Notifier/devices")]
    public sealed class NotifierDevicesController : ControllerBase
    {
        private const string OwnDevicesPermission = "blocks-logic::notifier::manage";

        private readonly IPushDeviceService _devices;

        /// <summary>Takes the device service.</summary>
        public NotifierDevicesController(IPushDeviceService devices)
        {
            _devices = devices;
        }

        /// <summary><c>POST</c> — registers (or refreshes) a device token of the caller. At most 10 per user; the oldest goes.</summary>
        [HttpPost]
        [ProtectedEndPoint(OwnDevicesPermission)]
        public Task<BaseResponse> RegisterDevice([FromBody] RegisterDeviceRequest request) =>
            _devices.RegisterAsync(request, HttpContext.RequestAborted);

        /// <summary><c>DELETE</c> — removes one of the caller's own device tokens. 404 when the caller has no such token.</summary>
        [HttpDelete("{token}")]
        [ProtectedEndPoint(OwnDevicesPermission)]
        public async Task<IActionResult> RemoveDevice([FromRoute] string token) =>
            await _devices.RemoveAsync(token, HttpContext.RequestAborted) ? NoContent() : NotFound();
    }
}
