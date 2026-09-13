using DomainService.People;
using Microsoft.AspNetCore.Mvc;
using Blocks.Genesis;
using Microsoft.AspNetCore.Authorization;

namespace Api.Controllers
{
    /// <summary>Directory lookups for the tenant's users, routed as <c>/api/People/{action}</c>.</summary>
    [ApiController]
    [Route("[controller]/[action]")]
    public class PeopleController : ControllerBase
    {
        private readonly IPeopleService _peopleService;

        /// <summary>Takes the people service.</summary>
        public PeopleController(IPeopleService peopleService)
        {
            _peopleService = peopleService;
        }

        /// <summary><c>POST</c> — the tenant's users, filtered and paged by the request body.</summary>
        [HttpPost]
        [Authorize]
        public async Task<GetPeoplesResponse> Gets([FromBody] GetPeoplesRequest command)
        {
            return await _peopleService.GetPeoplesAsync(command);
        }
    }
}