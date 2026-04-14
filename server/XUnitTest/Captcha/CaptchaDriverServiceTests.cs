using Captcha.DomainService.Captcha;
using FluentAssertions;
using Moq;

namespace XUnitTest.Captcha
{
    public class CaptchaDriverServiceTests
    {
        private readonly Mock<ICaptchaService> _captchaService;
        private readonly Blocks.CaptchaDriver.CaptchaDriverService _driverService;

        public CaptchaDriverServiceTests()
        {
            _captchaService = new Mock<ICaptchaService>();
            _driverService = new Blocks.CaptchaDriver.CaptchaDriverService(_captchaService.Object);
        }

        [Fact]
        public void Create_DelegatesToCaptchaService_AndReturnsResult()
        {
            // Arrange
            var request = new CreateCaptchaRequest { ConfigurationName = "test-config" };
            var expected = new CreateCaptchaRequestResponse(null)
            {
                Id = "captcha-123",
                Captcha = "ABC123"
            };

            _captchaService.Setup(x => x.CreateCaptcha(request)).Returns(expected);

            // Act
            var result = _driverService.Create(request);

            // Assert
            result.Should().BeSameAs(expected);
            _captchaService.Verify(x => x.CreateCaptcha(request), Times.Once);
        }

        [Fact]
        public async Task Submit_DelegatesToCaptchaService_AndReturnsResult()
        {
            // Arrange
            var request = new SubmitCaptchaRequest { Id = "captcha-123", Value = "ABC123" };
            var expected = new SubmitCaptchaRequestResponse(null) { VerificationCode = "verification-xyz" };

            _captchaService.Setup(x => x.SubmitCaptchaAsync(request)).ReturnsAsync(expected);

            // Act
            var result = await _driverService.Submit(request);

            // Assert
            result.Should().BeSameAs(expected);
            _captchaService.Verify(x => x.SubmitCaptchaAsync(request), Times.Once);
        }

        [Fact]
        public async Task Verify_DelegatesToCaptchaService_AndReturnsResult()
        {
            // Arrange
            var request = new VerifyCaptchaRequest { VerificationCode = "code-123", ConfigurationName = "recaptcha" };
            var expected = new VerifyCaptchaRequestResponse { Verified = true, HostName = "example.com" };

            _captchaService.Setup(x => x.VerifyCaptchaAsync(request)).ReturnsAsync(expected);

            // Act
            var result = await _driverService.Verify(request);

            // Assert
            result.Should().BeSameAs(expected);
            _captchaService.Verify(x => x.VerifyCaptchaAsync(request), Times.Once);
        }
    }
}
