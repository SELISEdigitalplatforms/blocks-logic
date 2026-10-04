using Blocks.Genesis;

using Mail.DomainService.Mails;
using Mail.DomainService.Template;
using Mail.DomainService.Template.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Api.Controllers
{
    /// <summary>Mail/notification template lookups, routed as <c>/api/Template/{action}</c>.</summary>
    [ApiController]
    [Route("[controller]/[action]")]

    public class TemplateController : ControllerBase
    {
        private readonly ITemplateService _templateService;

        /// <summary>Takes the template service.</summary>
        public TemplateController( ITemplateService templateService )
        {
            _templateService = templateService;
        }



        /// <summary><c>GET</c> — the tenant's templates, filtered by the query string.</summary>
        [HttpGet]
        [Authorize]
        public async Task<GetAllTemplatesResponse> Gets ( [FromQuery] GetAllTemplates request )
        {
            return await _templateService.GetAllTemplatesAsync(request);
        }
    }
}
