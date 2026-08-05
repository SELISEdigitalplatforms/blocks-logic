using Api.Controllers;
using Blocks.Genesis;
using DomainService.People;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Moq;

namespace XUnitTest.Controllers
{
    public class PeopleControllerTests
    {
        private readonly Mock<IPeopleService> _people = new();
        private readonly PeopleController _controller;

        public PeopleControllerTests()
        {
            _controller = new PeopleController(_people.Object);
        }

        [Fact]
        public async Task Gets_ReturnsResponse()
        {
            var response = new GetPeoplesResponse();
            _people.Setup(s => s.GetPeoplesAsync(It.IsAny<GetPeoplesRequest>())).ReturnsAsync(response);

            var result = await _controller.Gets(new GetPeoplesRequest());

            result.Should().BeSameAs(response);
        }
    }
}
