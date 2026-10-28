using Blocks.Genesis;
using DomainService.Notification.Push;
using Microsoft.AspNetCore.Mvc;

namespace Api.Controllers
{
    /// <summary>
    /// Every push device of the context tenant, for admins, routed as <c>/api/Notifier/devices/all</c>. Tenant DB only;
    /// tokens are never returned.
    /// </summary>
    [ApiController]
    [Route("Notifier/devices/all")]
    public sealed class NotifierDeviceAdminController : ControllerBase
    {
        private readonly IPushDeviceAdminService _devices;

        /// <summary>Takes the admin device service.</summary>
        public NotifierDeviceAdminController(IPushDeviceAdminService devices)
        {
            _devices = devices;
        }

        /// <summary><c>GET</c> — one page of devices, with a platform filter, a user search and counts per platform.</summary>
        [HttpGet]
        [ProtectedEndPoint("blocks-os::notification::gets")]
        public Task<PushDeviceList> GetAllDevices([FromQuery] PushDeviceListRequest request) =>
            _devices.ListAsync(request, HttpContext.RequestAborted);

        /// <summary><c>DELETE {id}</c> — removes any device of this tenant. 404 when there is none.</summary>
        [HttpDelete("{id}")]
        [ProtectedEndPoint("blocks-os::notification::save")]
        public async Task<IActionResult> RemoveAnyDevice([FromRoute] string id) =>
            await _devices.RemoveAsync(id, HttpContext.RequestAborted) ? NoContent() : NotFound();
    }
}
