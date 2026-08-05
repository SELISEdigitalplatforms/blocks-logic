using Blocks.EurolmDriver;
using Blocks.Genesis;
using Eurolm.DomainService.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BlocksTemplate.Api.Controllers
{
    [ApiController]
    [Route("[controller]/[action]")]
    public class LanguageController : ControllerBase
    {
        private readonly IEurolmDriverService _eurolmDriverService;

        public LanguageController(
            IEurolmDriverService eurolmDriverService)
        {
            _eurolmDriverService = eurolmDriverService;
        }

        /// <summary>
        /// Retrieves all available languages.
        /// </summary>
        /// <returns>A list of <see cref="Language"/> objects.</returns>
        [HttpGet]
        [Authorize]
        public async Task<List<Language>> Gets([FromQuery] GetLanguagesRequest request)
        {
            if (request == null) BadRequest();
            return await _eurolmDriverService.GetLanguagesAsync();
        }
    }
}
