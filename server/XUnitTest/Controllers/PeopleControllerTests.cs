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

        private static InviteRequest InviteWith(int count)
        {
            var req = new InviteRequest { GroupId = "g1" };
            for (var i = 0; i < count; i++)
                req.Invitations[$"proj{i}"] = new List<string> { "a@example.com" };
            return req;
        }

        [Fact]
        public async Task Invite_NoInvitations_ReturnsBadRequest()
        {
            var result = await _controller.Invite(InviteWith(0));
            result.Should().BeOfType<BadRequestObjectResult>();
        }

        [Fact]
        public async Task Invite_Success_ReturnsOk()
        {
            _people.Setup(s => s.InvitePeoplesAsync(It.IsAny<InviteRequest>()))
                .ReturnsAsync(new InviteResponse { IsSuccess = true });

            var result = await _controller.Invite(InviteWith(1));

            result.Should().BeOfType<OkObjectResult>();
        }

        [Fact]
        public async Task Invite_Failure_ReturnsBadRequest()
        {
            _people.Setup(s => s.InvitePeoplesAsync(It.IsAny<InviteRequest>()))
                .ReturnsAsync(new InviteResponse { IsSuccess = false });

            var result = await _controller.Invite(InviteWith(1));

            result.Should().BeOfType<BadRequestObjectResult>();
        }

        [Fact]
        public async Task RemoveAccess_Success_ReturnsOk()
        {
            _people.Setup(s => s.RemoveAccessFromProjectAsync(It.IsAny<RemoveAccessRequest>()))
                .ReturnsAsync(new BaseResponse { IsSuccess = true });

            var result = await _controller.RemoveAccess(new RemoveAccessRequest { GroupId = "g1" });

            result.Should().BeOfType<OkObjectResult>();
        }

        [Fact]
        public async Task Gets_ReturnsResponse()
        {
            var response = new GetPeoplesResponse();
            _people.Setup(s => s.GetPeoplesAsync(It.IsAny<GetPeoplesRequest>())).ReturnsAsync(response);

            var result = await _controller.Gets(new GetPeoplesRequest());

            result.Should().BeSameAs(response);
        }

        [Fact]
        public async Task ResendInvitation_Failure_ReturnsBadRequest()
        {
            _people.Setup(s => s.ResendInvitationAsync(It.IsAny<ResendInvitationRequest>()))
                .ReturnsAsync(new BaseResponse { IsSuccess = false });

            var result = await _controller.ResendInvitation(new ResendInvitationRequest());

            result.Should().BeOfType<BadRequestObjectResult>();
        }

        [Fact]
        public async Task ConfirmInvitation_Success_ReturnsOk()
        {
            _people.Setup(s => s.ConfirmInvitationAsync(It.IsAny<ConfirmInvitationRequest>()))
                .ReturnsAsync(new ConfirmInvitationResponse { IsSuccess = true });

            var result = await _controller.ConfirmInvitation(new ConfirmInvitationRequest());

            result.Should().BeOfType<OkObjectResult>();
        }

        [Fact]
        public async Task Signup_Success_ReturnsOk()
        {
            _people.Setup(s => s.SignupAsync(It.IsAny<SignupRequest>()))
                .ReturnsAsync(new SignupResponse { IsSuccess = true });

            var result = await _controller.Signup(new SignupRequest { Email = "a@example.com" });

            result.Should().BeOfType<OkObjectResult>();
        }

        [Fact]
        public async Task TransferOwnerShip_Failure_ReturnsBadRequest()
        {
            _people.Setup(s => s.TransferOwnershipAsync(It.IsAny<TransferOwnershipRequest>()))
                .ReturnsAsync(new BaseResponse { IsSuccess = false });

            var result = await _controller.TransferOwnerShip(new TransferOwnershipRequest());

            result.Should().BeOfType<BadRequestObjectResult>();
        }
    }
}
