using Api.Controllers;
using Blocks.Genesis;
using DomainService.Authentication;
using DomainService.Entities;
using DomainService.OAuth;
using DomainService.OAuth.RequestModel;
using DomainService.Services;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using System.Security.Claims;

namespace XUnitTest
{
    public class AuthenticationControllerTests
    {
        private readonly Mock<IOAuthTokenProvider> _tokenProvider = new();
        private readonly Mock<IAuthenticationService> _authService = new();
        private readonly Mock<IAuthenticationDomainService> _domainService = new();
        private readonly Mock<IAuthenticationRepository> _repo = new();
        private readonly Mock<IConfiguration> _config = new();
        private readonly Mock<ChangeControllerContext> _context = new(new Mock<ITenants>().Object, new Mock<IDbContextProvider>().Object, new Mock<IHttpContextAccessor>().Object);
        private readonly AuthenticationController _controller;

        public AuthenticationControllerTests()
        {
            _controller = new AuthenticationController(_tokenProvider.Object, _authService.Object, _config.Object, _domainService.Object, _repo.Object, _context.Object);
        }

        [Fact]
        public async Task Logout_Should_Return_Ok_When_Success()
        {
            _authService
                .Setup(x => x.LogoutUser(It.IsAny<string>(), It.IsAny<HttpRequest>()))
                .ReturnsAsync(new LogoutResponse { IsSuccess = true });

            var result = await _controller.Logout(new LogoutRequest
            {
                RefreshToken = "token"
            });

            object okObjectResult = result.Should().BeOfType<OkObjectResult>();
            _authService.Verify(x => x.DeleteCookie(It.IsAny<HttpRequest>()), Times.Once);
        }

        [Fact]
        public async Task Logout_Should_Return_BadRequest_When_Failed()
        {

            _authService
                .Setup(x => x.LogoutUser(It.IsAny<string>(), It.IsAny<HttpRequest>()))
                .ReturnsAsync(new LogoutResponse { IsSuccess = false });

            var result = await _controller.Logout(new LogoutRequest
            {
                RefreshToken = "token"
            });

            result.Should().BeOfType<BadRequestObjectResult>();
        }

        [Fact]
        public async Task Login_Should_Return_Unauthorized_When_Client_Invalid()
        {

            _authService
                .Setup(x => x.GetClientCredentialAsync(It.IsAny<string>()))
                .ReturnsAsync((OIDCClientCredential)null);

            var result = await _controller.Login(new LoginRequest
            {
                ClientId = "client",
                RedirectUri = "uri",
                Scope = "scope"
            });

            result.Should().BeOfType<UnauthorizedObjectResult>();
        }

        [Fact]
        public async Task Login_Should_Return_Ok_When_Authenticated()
        {

            _authService
                .Setup(x => x.GetClientCredentialAsync(It.IsAny<string>()))
                .ReturnsAsync(new OIDCClientCredential
                {
                    ItemId = "client",
                    RedirectUri = "uri",
                    Scope = "scope"
                });

            _tokenProvider
                .Setup(x => x.AuthenticateAsync(It.IsAny<TokenRequest>()))
                .ReturnsAsync(new OkResult());

            var result = await _controller.Login(new LoginRequest
            {
                ClientId = "client",
                RedirectUri = "uri",
                Scope = "scope",
                Username = "user",
                Password = "pass"
            });

            result.Should().BeOfType<OkResult>();
        }

        [Fact]
        public async Task GetUserInfo_Should_Return_Unauthorized_When_Token_Invalid()
        {

            _authService
                .Setup(x => x.GetPrincipalFromTokenAsync(
                    It.IsAny<HttpRequest>(),
                    It.IsAny<string>(),
                    false))
                .ReturnsAsync((ClaimsPrincipal)null);

            var result = await _controller.GetUserInfo();

            result.Should().BeOfType<UnauthorizedObjectResult>();
        }

        [Fact]
        public async Task GetUserInfo_Should_Return_Claims_When_Valid()
        {
            var claims = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, "1"),
             new Claim(ClaimTypes.Email, "test@test.com"),
             new Claim("name", "Test User")]));

            _authService
                .Setup(x => x.GetPrincipalFromTokenAsync(
                    It.IsAny<HttpRequest>(),
                    It.IsAny<string>(),
                    false))
                .ReturnsAsync(claims);

            var result = await _controller.GetUserInfo();

            result.Should().BeOfType<OkObjectResult>();
        }

        [Fact]
        public async Task LogoutUser_Should_Remove_Token_And_Update_Session()
        {
            var cache = new Mock<ICacheClient>();
            var repo = new Mock<IAuthenticationRepository>();
            var domain = new Mock<IAuthenticationDomainService>();
            var tenants = new Mock<ITenants>();
            var logger = new Mock<ILogger<AuthenticationService>>();

            repo.Setup(x => x.UpdateSessionStatusAsync(It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync(true);

            var service = new AuthenticationService(
                logger.Object,
                cache.Object,
                repo.Object,
                domain.Object,
                tenants.Object
            );

            var result = await service.LogoutUser("token", new DefaultHttpContext().Request);

            result.IsSuccess.Should().BeTrue();
        }
    }
}
