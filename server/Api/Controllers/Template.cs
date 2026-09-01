using Blocks.Genesis;

using Mail.DomainService.Mails;
using Mail.DomainService.Template;
using Mail.DomainService.Template.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Api.Controllers
{
    [ApiController]
    [Route("[controller]/[action]")]

    public class TemplateController : ControllerBase
    {
        private readonly ITemplateService _templateService;

        public TemplateController( ITemplateService templateService )
        {
            _templateService = templateService;
        }



        [HttpGet]
        [Authorize]
        public async Task<GetAllTemplatesResponse> Gets ( [FromQuery] GetAllTemplates request )
        {
            return await _templateService.GetAllTemplatesAsync(request);
        }
    }
}
