using Common.InternalService.Language;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BlocksTemplate.Api.Controllers
{
    /// <summary>Language reference data, routed as <c>/api/Language/{action}</c>.</summary>
    [ApiController]
    [Route("[controller]/[action]")]
    public class LanguageController : ControllerBase
    {
        private readonly ILanguageManagementService _languageManagementService;

        /// <summary>Takes the language management service.</summary>
        public LanguageController(
            ILanguageManagementService languageManagementService)
        {
            _languageManagementService = languageManagementService;
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
            return await _languageManagementService.GetLanguagesAsync();
        }
    }
}
