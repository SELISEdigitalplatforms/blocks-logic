using Blocks.MfaDriver;
using FluentAssertions;
using Iam.DomainService.Entities;
using Mfa.DomainService.Services;
using Mfa.DomainService.Shared;
using Moq;

namespace XUnitTest.Drivers
{
    public class MfaDriverServiceTests
    {
        private readonly Mock<IMfaManagementService> _mfaManagementService;
        private readonly MfaDriverService _driverService;

        public MfaDriverServiceTests()
        {
            _mfaManagementService = new Mock<IMfaManagementService>();
            _driverService = new MfaDriverService(_mfaManagementService.Object);
        }

        [Fact]
        public async Task GenerateOtpAsync_DelegatesToMfaManagementService()
        {
            // Arrange
            var request = new OtpGenerationRequest
            {
                UserId = "user-123",
                MfaType = UserMfaType.Email,
                SendPhoneNumberAsEmailDomain = "example.com"
            };
            var expectedResponse = new OtpGenerationResponse
            {
                IsSuccess = true,
                MfaId = "mfa-456"
            };

            _mfaManagementService
                .Setup(x => x.GenerateOTPAsync(request))
                .ReturnsAsync(expectedResponse);

            // Act
            var result = await _driverService.GenerateOtpAsync(request);

            // Assert
            result.Should().NotBeNull();
            result.Should().Be(expectedResponse);
            result.IsSuccess.Should().BeTrue();
            result.MfaId.Should().Be("mfa-456");
            _mfaManagementService.Verify(x => x.GenerateOTPAsync(request), Times.Once);
        }

        [Fact]
        public async Task VerifyOtpAsync_DelegatesToMfaManagementService()
        {
            // Arrange
            var request = new VerifyOtpRequest
            {
                VerificationCode = "123456",
                MfaId = "mfa-789",
                AuthType = UserMfaType.TOTP,
                IsFromTokenCall = false
            };
            var expectedResponse = new OtpVerificationResponse
            {
                IsSuccess = true,
                IsValid = true,
                UserId = "user-123"
            };

            _mfaManagementService
                .Setup(x => x.VerifyOTPAsync(request))
                .ReturnsAsync(expectedResponse);

            // Act
            var result = await _driverService.VerifyOtpAsync(request);

            // Assert
            result.Should().NotBeNull();
            result.Should().Be(expectedResponse);
            result.IsSuccess.Should().BeTrue();
            result.IsValid.Should().BeTrue();
            result.UserId.Should().Be("user-123");
            _mfaManagementService.Verify(x => x.VerifyOTPAsync(request), Times.Once);
        }

        [Fact]
        public async Task GenerateOtpAsync_WithFailedResponse_ReturnsFailureResponse()
        {
            // Arrange
            var request = new OtpGenerationRequest { UserId = "user-123" };
            var expectedResponse = new OtpGenerationResponse
            {
                IsSuccess = false,
                Errors = new Dictionary<string, string>
                {
                    { "error", "MFA not enabled" }
                }
            };

            _mfaManagementService
                .Setup(x => x.GenerateOTPAsync(request))
                .ReturnsAsync(expectedResponse);

            // Act
            var result = await _driverService.GenerateOtpAsync(request);

            // Assert
            result.Should().NotBeNull();
            result.IsSuccess.Should().BeFalse();
            result.Errors.Should().ContainKey("error");
            _mfaManagementService.Verify(x => x.GenerateOTPAsync(request), Times.Once);
        }

        [Fact]
        public async Task VerifyOtpAsync_WithInvalidCode_ReturnsInvalidResponse()
        {
            // Arrange
            var request = new VerifyOtpRequest
            {
                VerificationCode = "000000",
                MfaId = "mfa-123",
                AuthType = UserMfaType.Email
            };
            var expectedResponse = new OtpVerificationResponse
            {
                IsSuccess = true,
                IsValid = false,
                UserId = "user-123"
            };

            _mfaManagementService
                .Setup(x => x.VerifyOTPAsync(request))
                .ReturnsAsync(expectedResponse);

            // Act
            var result = await _driverService.VerifyOtpAsync(request);

            // Assert
            result.Should().NotBeNull();
            result.IsSuccess.Should().BeTrue();
            result.IsValid.Should().BeFalse();
            _mfaManagementService.Verify(x => x.VerifyOTPAsync(request), Times.Once);
        }
    }
}
