using System.Linq.Expressions;
using System.Net;
using Blocks.Genesis;
using FluentAssertions;
using FluentValidation;
using FluentValidation.Results;
using DomainService.Storage;
using Mfa.DomainService.Entities;
using Mfa.DomainService.Services;
using Mfa.DomainService.Shared;
using Mfa.DomainService.TOTP;
using StorageDriver;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using OtpNet;

namespace XUnitTest.Mfa
{
    /// <summary>
    /// Unit tests for <see cref="TotpService"/>. The enrolment path builds a QR code, uploads it to
    /// object storage and only then records the secret, so the branches that matter are the ones
    /// that stop short: an unknown user, storage that is not configured, a rejected upload. The
    /// verification path is guarded first by the validator and then by a login session that expires.
    /// </summary>
    public class TotpServiceTests : IDisposable
    {
        private readonly Mock<IMfaManagementRepository> _repository = new();
        private readonly Mock<ICacheClient> _cache = new();
        private readonly Mock<IValidator<VerifyOtpRequest>> _validator = new();
        private readonly Mock<ITenants> _tenants = new();
        private readonly Mock<IStorageDriverService> _storage = new();
        private readonly Mock<IHttpContextAccessor> _httpContext = new();

        public TotpServiceTests()
        {
            BlocksContext.IsTestMode = true;
            BlocksContext.SetContext(BlocksContext.Create(
                "tenant-1", null, "user-1", true, null, null,
                DateTime.UtcNow.AddHours(1), null, null, null, null, null, null, "", "tenant-1"));

            _validator.Setup(v => v.ValidateAsync(It.IsAny<VerifyOtpRequest>(), It.IsAny<CancellationToken>()))
                      .ReturnsAsync(new ValidationResult());
            _cache.Setup(c => c.AddStringValueAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<long>()))
                  .ReturnsAsync(true);
        }

        public void Dispose()
        {
            BlocksContext.SetContext(null);
            BlocksContext.IsTestMode = false;
        }

        private TotpService CreateService() => new(
            _repository.Object,
            new Mock<ILogger<TotpService>>().Object,
            _httpContext.Object,
            new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>()).Build(),
            _cache.Object,
            _validator.Object,
            _tenants.Object,
            _storage.Object);

        private void SetupUser(UserInfo? user) =>
            _repository.Setup(r => r.GetItemAsync<UserInfo>(
                          It.IsAny<Expression<Func<UserInfo, bool>>>(), "Users"))
                      .ReturnsAsync(user);

        private void SetupTotpDetail(UserTotpDetail? detail) =>
            _repository.Setup(r => r.GetItemAsync<UserTotpDetail>(
                          It.IsAny<Expression<Func<UserTotpDetail, bool>>>(), It.IsAny<string>()))
                      .ReturnsAsync(detail);

        // ---- GenerateAsync ----

        [Fact]
        public async Task GenerateAsync_IssuesAnMfaIdAndOpensALoginSession()
        {
            var result = await CreateService().GenerateAsync(new UserInfo { ItemId = "user-1" });

            result.IsSuccess.Should().BeTrue();
            result.MfaId.Should().NotBeNullOrWhiteSpace();
            _cache.Verify(c => c.AddStringValueAsync(result.MfaId, "user-1", 15 * 60), Times.Once);
        }

        [Fact]
        public async Task GenerateAsync_IssuesADifferentMfaIdEachTime()
        {
            var service = CreateService();

            var first = await service.GenerateAsync(new UserInfo { ItemId = "user-1" });
            var second = await service.GenerateAsync(new UserInfo { ItemId = "user-1" });

            second.MfaId.Should().NotBe(first.MfaId);
        }

        // ---- enrolment ----

        [Fact]
        public async Task GenerateTotpImageByUserAsync_ReportsAnUnknownUser()
        {
            SetupUser(null);

            var result = await CreateService().GenerateTotpImageByUserAsync("nobody");

            result.IsSuccess.Should().BeFalse();
            result.Errors.Should().ContainKey("user_not_exist");
        }

        [Fact]
        public async Task GenerateTotpImageByUserAsync_ReusesAnEnrolmentThatAlreadyExists()
        {
            SetupUser(new UserInfo { ItemId = "user-1", Email = "ada@example.com" });
            SetupTotpDetail(new UserTotpDetail
            {
                CreatedBy = "user-1",
                Secret = "SECRET",
                ImageUri = "https://cdn.example.com/qr.png",
            });

            var result = await CreateService().GenerateTotpImageByUserAsync("user-1");

            result.IsSuccess.Should().BeTrue();
            result.QrImageUrl.Should().Be("https://cdn.example.com/qr.png");
            result.QrCode.Should().Be("SECRET");
            // Nothing is re-uploaded and no new secret is written.
            _storage.Verify(s => s.GetPerSignedUrlForUploadAsync(It.IsAny<GetPreSignedUrlForUploadRequest>()), Times.Never);
        }

        [Fact]
        public async Task GenerateTotpImageByUserAsync_EnrolsAgainWhenTheStoredImageIsMissing()
        {
            SetupUser(new UserInfo { ItemId = "user-1", Email = "ada@example.com" });
            SetupTotpDetail(new UserTotpDetail { CreatedBy = "user-1", Secret = "SECRET", ImageUri = "" });
            _storage.Setup(s => s.GetPerSignedUrlForUploadAsync(It.IsAny<GetPreSignedUrlForUploadRequest>()))
                    .ReturnsAsync(new GetPreSignedUrlForUploadResponse { UploadUrl = string.Empty });

            var result = await CreateService().GenerateTotpImageByUserAsync("user-1");

            // It gets as far as needing storage, rather than handing back the empty image.
            result.IsSuccess.Should().BeFalse();
            result.Errors.Should().ContainKey("configuration_not_exit");
        }

        [Fact]
        public async Task GenerateTotpImageByUserAsync_ReportsStorageThatIsNotConfigured()
        {
            SetupUser(new UserInfo { ItemId = "user-1", Email = "ada@example.com" });
            SetupTotpDetail(null);
            _storage.Setup(s => s.GetPerSignedUrlForUploadAsync(It.IsAny<GetPreSignedUrlForUploadRequest>()))
                    .ReturnsAsync(new GetPreSignedUrlForUploadResponse { UploadUrl = string.Empty });

            var result = await CreateService().GenerateTotpImageByUserAsync("user-1");

            result.IsSuccess.Should().BeFalse();
            result.Errors.Should().ContainKey("configuration_not_exit");
            // Crucially, no secret is recorded for a user who never received a QR code.
            _repository.Verify(r => r.SaveAsync(It.IsAny<UserTotpDetail>(), It.IsAny<string>()), Times.Never);
        }

        // ---- verification ----

        [Fact]
        public async Task VerifyAsync_RefusesAnInvalidRequestBeforeTouchingTheSession()
        {
            _validator.Setup(v => v.ValidateAsync(It.IsAny<VerifyOtpRequest>(), It.IsAny<CancellationToken>()))
                      .ReturnsAsync(new ValidationResult(new[]
                      {
                          new ValidationFailure("VerificationCode", "VerificationCode is required"),
                      }));

            var result = await CreateService().VerifyAsync(new VerifyOtpRequest());

            result.IsSuccess.Should().BeFalse();
            result.Errors.Should().ContainKey("VerificationCode");
            _cache.Verify(c => c.KeyExistsAsync(It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task VerifyAsync_ReportsAnExpiredLoginSession()
        {
            _cache.Setup(c => c.KeyExistsAsync("mfa-1")).ReturnsAsync(false);

            var result = await CreateService().VerifyAsync(
                new VerifyOtpRequest { MfaId = "mfa-1", VerificationCode = "123456" });

            result.IsSuccess.Should().BeFalse();
            result.Errors.Should().ContainKey("login_session_expired");
        }

        [Fact]
        public async Task VerifyAsync_AcceptsTheCodeTheStoredSecretCurrentlyProduces()
        {
            var secret = Base32Encoding.ToString(KeyGeneration.GenerateRandomKey(20));
            _cache.Setup(c => c.KeyExistsAsync("mfa-1")).ReturnsAsync(true);
            _cache.Setup(c => c.GetStringValueAsync("mfa-1")).ReturnsAsync("user-1");
            SetupTotpDetail(new UserTotpDetail { CreatedBy = "user-1", Secret = secret });
            var current = new Totp(Base32Encoding.ToBytes(secret)).ComputeTotp();

            var result = await CreateService().VerifyAsync(
                new VerifyOtpRequest { MfaId = "mfa-1", VerificationCode = current });

            result.IsSuccess.Should().BeTrue();
            result.IsValid.Should().BeTrue();
            result.UserId.Should().Be("user-1");
        }

        [Fact]
        public async Task VerifyAsync_RejectsACodeTheStoredSecretDidNotProduce()
        {
            var secret = Base32Encoding.ToString(KeyGeneration.GenerateRandomKey(20));
            _cache.Setup(c => c.KeyExistsAsync("mfa-1")).ReturnsAsync(true);
            _cache.Setup(c => c.GetStringValueAsync("mfa-1")).ReturnsAsync("user-1");
            SetupTotpDetail(new UserTotpDetail { CreatedBy = "user-1", Secret = secret });

            var result = await CreateService().VerifyAsync(
                new VerifyOtpRequest { MfaId = "mfa-1", VerificationCode = "000000" });

            // The call succeeded; the code simply did not match.
            result.IsSuccess.Should().BeTrue();
            result.IsValid.Should().BeFalse();
        }

        [Fact]
        public async Task VerifyAsync_ResolvesTheUserFromTheSessionRatherThanTheRequest()
        {
            var secret = Base32Encoding.ToString(KeyGeneration.GenerateRandomKey(20));
            _cache.Setup(c => c.KeyExistsAsync("mfa-1")).ReturnsAsync(true);
            _cache.Setup(c => c.GetStringValueAsync("mfa-1")).ReturnsAsync("user-from-session");
            SetupTotpDetail(new UserTotpDetail { CreatedBy = "user-from-session", Secret = secret });

            var result = await CreateService().VerifyAsync(
                new VerifyOtpRequest { MfaId = "mfa-1", VerificationCode = "000000" });

            // A caller cannot verify against someone else's enrolment by naming them.
            result.UserId.Should().Be("user-from-session");
            _cache.Verify(c => c.GetStringValueAsync("mfa-1"), Times.Once);
        }
    }
}
