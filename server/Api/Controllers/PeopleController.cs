using DomainService.People;
using Microsoft.AspNetCore.Mvc;
using Blocks.Genesis;
using Microsoft.AspNetCore.Authorization;

namespace Api.Controllers
{
    [ApiController]
    [Route("[controller]/[action]")]
    public class PeopleController : ControllerBase
    {
        private readonly IPeopleService _peopleService;

        public PeopleController(IPeopleService peopleService)
        {
            _peopleService = peopleService;
        }

        [HttpPost]
        [Authorize]
        public async Task<GetPeoplesResponse> Gets([FromBody] GetPeoplesRequest command)
        {
            return await _peopleService.GetPeoplesAsync(command);
        }
    }
}