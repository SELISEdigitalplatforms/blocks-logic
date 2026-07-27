using Blocks.Genesis;
using Captcha.DomainService.Captcha;
using Captcha.DomainService.Configuration;
using DomainService.People;
using DomainService.Shared.Entities;
using FluentAssertions;
using FluentValidation.TestHelper;
using MongoDB.Driver;
using Moq;

namespace XUnitTest.Identifier
{
    public class SignupRequestValidatorTests
    {
        private readonly Mock<ICaptchaService> _captcha = new();
        private readonly Mock<IDbContextProvider> _dbContext = new();
        private readonly Mock<IPeopleRepository> _people = new();
        private readonly Mock<IMongoCollection<CaptchaConfiguration>> _captchaCollection = new();

        public SignupRequestValidatorTests()
        {
            _people.Setup(r => r.GetSignUpSettingAsync())
                .ReturnsAsync(new SignUpSetting { IsEmailPasswordSignUpEnabled = true });
            SetupCaptchaConfig(null);
        }

        private SignupRequestValidator CreateValidator() =>
            new(_captcha.Object, _dbContext.Object, _people.Object);

        private void SetupCaptchaConfig(CaptchaConfiguration? config)
        {
            var cursor = new Mock<IAsyncCursor<CaptchaConfiguration>>();
            cursor.Setup(x => x.Current).Returns(config != null ? new[] { config } : Array.Empty<CaptchaConfiguration>());
            cursor.SetupSequence(x => x.MoveNext(It.IsAny<CancellationToken>()))
                .Returns(config != null).Returns(false);
            cursor.SetupSequence(x => x.MoveNextAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(config != null).ReturnsAsync(false);

            _captchaCollection.Setup(x => x.FindAsync(
                It.IsAny<FilterDefinition<CaptchaConfiguration>>(),
                It.IsAny<FindOptions<CaptchaConfiguration, CaptchaConfiguration>>(),
                It.IsAny<CancellationToken>()))
                .ReturnsAsync(cursor.Object);

            _dbContext.Setup(x => x.GetCollection<CaptchaConfiguration>("CaptchaConfigurations"))
                .Returns(_captchaCollection.Object);
        }

        [Fact]
        public async Task ValidEmail_SignupEnabled_CaptchaDisabled_Passes()
        {
            var result = await CreateValidator().TestValidateAsync(new SignupRequest { Email = "user@example.com" });
            result.IsValid.Should().BeTrue();
        }

        [Fact]
        public async Task EmptyEmail_Fails()
        {
            var result = await CreateValidator().TestValidateAsync(new SignupRequest { Email = "" });
            result.ShouldHaveValidationErrorFor(x => x.Email);
        }

        [Fact]
        public async Task InvalidEmail_Fails()
        {
            var result = await CreateValidator().TestValidateAsync(new SignupRequest { Email = "nope" });
            result.ShouldHaveValidationErrorFor(x => x.Email);
        }

        [Fact]
        public async Task SignupDisabled_Fails()
        {
            _people.Setup(r => r.GetSignUpSettingAsync())
                .ReturnsAsync(new SignUpSetting { IsEmailPasswordSignUpEnabled = false });
            var result = await CreateValidator().TestValidateAsync(new SignupRequest { Email = "user@example.com" });
            result.ShouldHaveValidationErrorFor(x => x.Email);
        }

        [Fact]
        public async Task SignupSettingNull_Fails()
        {
            _people.Setup(r => r.GetSignUpSettingAsync()).ReturnsAsync((SignUpSetting)null!);
            var result = await CreateValidator().TestValidateAsync(new SignupRequest { Email = "user@example.com" });
            result.ShouldHaveValidationErrorFor(x => x.Email);
        }

        [Fact]
        public async Task CaptchaEnabled_InvalidCode_Fails()
        {
            SetupCaptchaConfig(new CaptchaConfiguration { Provider = "recaptcha", IsEnable = true });
            _captcha.Setup(x => x.VerifyCaptchaAsync(It.IsAny<VerifyCaptchaRequest>()))
                .ReturnsAsync(new VerifyCaptchaRequestResponse { Verified = false });

            var result = await CreateValidator().TestValidateAsync(
                new SignupRequest { Email = "user@example.com", CaptchaCode = "bad" });
            result.ShouldHaveValidationErrorFor(x => x.CaptchaCode);
        }

        [Fact]
        public async Task CaptchaEnabled_ValidCode_Passes()
        {
            SetupCaptchaConfig(new CaptchaConfiguration { Provider = "recaptcha", IsEnable = true });
            _captcha.Setup(x => x.VerifyCaptchaAsync(It.IsAny<VerifyCaptchaRequest>()))
                .ReturnsAsync(new VerifyCaptchaRequestResponse { Verified = true });

            var result = await CreateValidator().TestValidateAsync(
                new SignupRequest { Email = "user@example.com", CaptchaCode = "good" });
            result.ShouldNotHaveValidationErrorFor(x => x.CaptchaCode);
        }
    }
}
