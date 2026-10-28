using Blocks.Genesis;
using DomainService.Notification.Push;
using Microsoft.AspNetCore.Mvc;

namespace Api.Controllers
{
    /// <summary>
    /// The project's Firebase (mobile push) settings, routed as <c>/api/Notifier/push-settings</c> (route fixed by the
    /// os UI), with the permissions of the os notification configuration endpoints. A refused save answers 200 with
    /// <c>{ isSuccess: false, errors }</c>. The service-account key is never returned.
    /// </summary>
    [ApiController]
    [Route("Notifier/push-settings")]
    public sealed class NotifierPushSettingsController : ControllerBase
    {
        private const string SavePermission = "blocks-os::notification::save";

        private readonly IPushSettingsService _settings;

        /// <summary>Takes the push settings service.</summary>
        public NotifierPushSettingsController(IPushSettingsService settings)
        {
            _settings = settings;
        }

        /// <summary><c>GET</c> — project id, on/off and whether a key is stored.</summary>
        [HttpGet]
        [ProtectedEndPoint("blocks-os::notification::gets")]
        public Task<PushSettingsView> GetPushSettings() => _settings.GetAsync(HttpContext.RequestAborted);

        /// <summary><c>POST</c> — saves the settings; a new service-account key goes to Blocks Secrets.</summary>
        [HttpPost]
        [ProtectedEndPoint(SavePermission)]
        public Task<BaseResponse> SavePushSettings([FromBody] SavePushSettingsRequest request) =>
            _settings.SaveAsync(request, HttpContext.RequestAborted);

        /// <summary><c>POST</c> — pushes a fixed test text to the caller's own devices.</summary>
        [HttpPost("test")]
        [ProtectedEndPoint(SavePermission)]
        public Task<BaseResponse> SendPushTest() => _settings.SendTestAsync(HttpContext.RequestAborted);
    }
}
