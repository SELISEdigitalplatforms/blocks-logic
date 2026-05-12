using Blocks.Genesis;
using Blocks.MailDriver;
using Microsoft.AspNetCore.Mvc;

namespace Api.Controllers
{
    [ApiController]
    [Route("[controller]/[action]")]

    public class TemplateController : ControllerBase
    {
        private readonly IMailDriverService _templateService;
        private readonly ChangeControllerContext _changeControllerContext;

        public TemplateController(IMailDriverService templateService, ChangeControllerContext changeControllerContext)
        {
            _templateService = templateService;
            _changeControllerContext = changeControllerContext;
        }

       

        [HttpGet]
        [ProtectedEndPoint]
        public async Task<GetAllTemplatesResponse> Gets([FromQuery] GetAllTemplates request)
        {
            _changeControllerContext.ChangeContext(request);
            return await _templateService.GetAllTemplatesAsync(request);
        }
    }
}
