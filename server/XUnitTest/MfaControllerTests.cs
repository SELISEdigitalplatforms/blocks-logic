using Api.Controllers;
using Blocks.Genesis;
using FluentAssertions;
using FluentValidation;
using Iam.DomainService.Entities;
using Mfa.DomainService.Services;
using Mfa.DomainService.Shared;
using Mfa.DomainService.TOTP;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;

namespace XUnitTest
{
    public class MfaControllerTests
    {
        private readonly Mock<IMfaManagementService> _mfaService = new();
        private readonly Mock<TotpService> _totpService;
        private readonly Mock<ChangeControllerContext> _changeContext;
        private readonly MfaController _controller;

        public MfaControllerTests()
        {
            _totpService = new Mock<TotpService>(Mock.Of<IMfaManagementRepository>(), Mock.Of<ILogger<TotpService>>(), Mock.Of<IHttpContextAccessor>(), Mock.Of<IConfiguration>(), Mock.Of<ICacheClient>(), Mock.Of<IValidator<VerifyOtpRequest>>(), Mock.Of<ITenants>());
            _changeContext = new Mock<ChangeControllerContext>( Mock.Of<ITenants>(),Mock.Of<IDbContextProvider>(), Mock.Of<IHttpContextAccessor>());
            _controller = new MfaController(_mfaService.Object, _totpService.Object, _changeContext.Object);
        }

        [Fact]
        public async Task GenerateOTP_ReturnsResponse_And_ChangesContext()
        {
            // Arrange
            var request = new OtpGenerationRequest
            {
                UserId = "user-1"
            };

            var response = new OtpGenerationResponse
            {
                IsSuccess = true
            };

            _mfaService
                .Setup(x => x.GenerateOTPAsync(request))
                .ReturnsAsync(response);

            // Act
            var result = await _controller.GenerateOTP(request);

            // Assert
            result.Should().Be(response);
        }

        [Fact]
        public async Task VerifyOTP_ReturnsResponse_And_ChangesContext()
        {
            // Arrange
            var request = new VerifyOtpRequest
            {
                AuthType = UserMfaType.Email
            };

            var response = new OtpVerificationResponse
            {
                IsValid = true
            };

            _mfaService
                .Setup(x => x.VerifyOTPAsync(request))
                .ReturnsAsync(response);

            // Act
            var result = await _controller.VerifyOTP(request);

            // Assert
            result.Should().Be(response);
        }

        [Fact]
        public async Task DisableUserMfa_ReturnsSuccess()
        {
            // Arrange
            var request = new DisableUserMfaRequest
            {
                UserId = "user-1"
            };

            var response = new BaseResponse
            {
                IsSuccess = true
            };

            _mfaService
                .Setup(x => x.DisableUserMfa(request))
                .ReturnsAsync(response);

            // Act
            var result = await _controller.DisableUserMfa(request);

            // Assert
            result.Should().Be(response);
        }
    }
}
