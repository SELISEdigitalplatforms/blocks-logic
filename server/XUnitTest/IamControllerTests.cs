using Api.Controllers;
using Blocks.Genesis;
using FluentAssertions;
using Iam.DomainService.Accounts;
using Iam.DomainService.Activities;
using Iam.DomainService.Resources;
using Iam.DomainService.Users;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;

namespace XUnitTest
{
    public class IamControllerTests
    {
        private readonly Mock<IAccountService> _accountService = new();
        private readonly Mock<IUserActivityService> _activityService = new();
        private readonly Mock<IUserManagementQueryService> _userQueryService = new();
        private readonly Mock<IUserManagementMutationService> _userMutationService = new();
        private readonly Mock<IResourceMutationService> _resourceMutationService = new();
        private readonly Mock<IResourceQueryService> _resourceQueryService = new();
        private readonly Mock<ChangeControllerContext> _changeContext = new(new Mock<ITenants>().Object, new Mock<IDbContextProvider>().Object, new Mock<IHttpContextAccessor>().Object);
        private readonly IamController _controller;

        public IamControllerTests()
        {
            _controller = new IamController(_accountService.Object, _activityService.Object, _resourceMutationService.Object, _resourceQueryService.Object, _userQueryService.Object, _userMutationService.Object, _changeContext.Object);
        }

        private IamController CreateController()
        {
            var controller = new IamController(
                _accountService.Object,
                _activityService.Object,
                _resourceMutationService.Object,
                _resourceQueryService.Object,
                _userQueryService.Object,
                _userMutationService.Object,
                _changeContext.Object
            );

            controller.ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            };

            return controller;
        }

        [Fact]
        public async Task Activate_WhenSuccess_ReturnsOk()
        {
            // Arrange
            var command = new ActivateUserRequest
            {
                Code = "code"
            };

            var serviceResponse = new BaseAccountResponse
            {
                IsSuccess = true
            };

            _accountService
                .Setup(x => x.ActivateAccountAsync(command))
                .ReturnsAsync(serviceResponse);

            var controller = CreateController();

            // Act
            var result = await controller.Activate(command);

            // Assert
            var ok = result.Should().BeOfType<OkObjectResult>().Subject;
            ok.Value.Should().Be(serviceResponse);
        }

        [Fact]
        public async Task Activate_WhenFailure_ReturnsBadRequest()
        {
            // Arrange
            var command = new ActivateUserRequest
            {
                Code = "invalid"
            };

            var serviceResponse = new BaseAccountResponse
            {
                IsSuccess = false,
                Errors = new Dictionary<string, string>{ { "Code", "Invalid" }}};

            _accountService
                .Setup(x => x.ActivateAccountAsync(command))
                .ReturnsAsync(serviceResponse);

            var controller = CreateController();

            // Act
            var result = await controller.Activate(command);

            // Assert
            var badRequest = result.Should().BeOfType<BadRequestObjectResult>().Subject;
            badRequest.Value.Should().Be(serviceResponse);
        }

        [Fact]
        public async Task Recover_WhenValid_ReturnsOk()
        {
            var command = new RecoveryUserRequest
            {
                Email = "test@test.com"
            };

            var response = new BaseAccountResponse
            {
                IsSuccess = true
            };

            _accountService
                .Setup(x => x.RecoverAccountAsync(command))
                .ReturnsAsync(response);

            var controller = CreateController();

            var result = await controller.Recover(command);

            result.Should().BeOfType<OkObjectResult>();
        }

    }
}
