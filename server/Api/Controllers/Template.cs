using Blocks.Genesis;
using Blocks.MailDriver;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Api.Controllers
{
    [ApiController]
    [Route("[controller]/[action]")]

    public class TemplateController : ControllerBase
    {
        private readonly IMailDriverService _templateService;

        public TemplateController(IMailDriverService templateService)
        {
            _templateService = templateService;
        }

       

        [HttpGet]
        //[ProtectedEndPoint]
        [Authorize]
        public async Task<GetAllTemplatesResponse> Gets([FromQuery] GetAllTemplates request)
        {
            return await _templateService.GetAllTemplatesAsync(request);
        }
    }
}
