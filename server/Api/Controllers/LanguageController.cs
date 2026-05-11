using Blocks.EurolmDriver;
using Blocks.Genesis;
using Eurolm.DomainService.Services;
using Microsoft.AspNetCore.Mvc;

namespace BlocksTemplate.Api.Controllers
{
    [ApiController]
    [Route("[controller]/[action]")]
    public class LanguageController : ControllerBase
    {
        private readonly IEurolmDriverService _eurolmDriverService;
        private readonly ChangeControllerContext _changeControllerContext;

        public LanguageController(
            IEurolmDriverService eurolmDriverService,
            ChangeControllerContext changeControllerContext)
        {
            _eurolmDriverService = eurolmDriverService;
            _changeControllerContext = changeControllerContext;
        }

        /// <summary>
        /// Retrieves all available languages.
        /// </summary>
        /// <returns>A list of <see cref="Language"/> objects.</returns>
        [HttpGet]
        public async Task<List<Language>> Gets([FromQuery] GetLanguagesRequest request)
        {
            if (request == null) BadRequest();
            _changeControllerContext.ChangeContext(request);
            return await _eurolmDriverService.GetLanguagesAsync();
        }
    }
}
