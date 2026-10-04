using MailBoxSyncService.Services;
using Microsoft.AspNetCore.Mvc;

namespace MailBoxSyncService.Controllers
{
    [ApiController]
    [Route("[controller]/[action]")]
    public class SesEventsController : ControllerBase
    {
        private readonly ISnsEventProcessor _snsEventProcessor;

        public SesEventsController(
            ISnsEventProcessor snsEventProcessor,
            ILogger<SesEventsController> logger)
        {
            _snsEventProcessor = snsEventProcessor;
        }

        [HttpPost]
        public async Task<IActionResult> Receive()
        {
            await _snsEventProcessor.ProcessAsync(Request);
            return Ok();
        }
    }
}
