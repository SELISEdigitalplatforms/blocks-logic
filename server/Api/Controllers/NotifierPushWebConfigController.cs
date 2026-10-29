using Blocks.Genesis;
using DomainService.Notification.Push;
using Microsoft.AspNetCore.Mvc;

namespace Api.Controllers
{
    /// <summary>
    /// The Firebase web config for signed-in web apps of the context tenant, routed as
    /// <c>/api/Notifier/push-web-config</c> (a browser needs it to register for push). Never cached, never logged.
    /// </summary>
    [ApiController]
    [Route("Notifier/push-web-config")]
    public sealed class NotifierPushWebConfigController : ControllerBase
    {
        private readonly IPushWebConfigService _webConfig;

        /// <summary>Takes the web config service.</summary>
        public NotifierPushWebConfigController(IPushWebConfigService webConfig)
        {
            _webConfig = webConfig;
        }

        /// <summary><c>GET</c> — the web config when push and Web are on; else <c>{ isSuccess: false }</c>.</summary>
        [HttpGet]
        [ProtectedEndPoint("blocks-logic::notifier::manage")]
        public Task<PushWebConfigResponse> GetPushWebConfig()
        {
            Response.Headers.CacheControl = "no-store";
            return _webConfig.GetAsync(HttpContext.RequestAborted);
        }
    }
}
