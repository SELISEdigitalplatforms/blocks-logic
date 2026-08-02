using Blocks.Genesis;
using Blocks.MailDriver;
using DomainService.Dtos;
using DomainService.Migration;
using DomainService.Migration.Entities;
using DomainService.Migration.Services;
using DomainService.Shared;
using FluentAssertions;
using FluentValidation;
using FluentValidation.Results;
using Iam.DomainService.Entities;
using Iam.DomainService.Users;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using System.Text.Json;
using XUnitTest.TestHelpers;
using SendMail = Blocks.MailDriver.SendMail;

namespace XUnitTest.Identifier
{
    public class MigrationServiceTests : IDisposable
    {
        private readonly Mock<ICacheClient> _cacheClient = new();
        private readonly Mock<IMailDriverService> _mailDriverService = new();
        private readonly Mock<IMessageClient> _messageClient = new();
        private readonly Mock<IValidator<MigrationRequest>> _validator = new();
        private readonly Mock<IMigrationRepository> _migrationRepository = new();
        private readonly Mock<IConfiguration> _configuration = new();
        private readonly Mock<ITenants> _tenants = new();
        private readonly Mock<ICryptoService> _cryptoService = new();
        private readonly Mock<IHttpService> _httpService = new();
        private readonly Mock<IUserRepository> _userRepository = new();
        private readonly Mock<ILogger<MigrationService>> _logger = new();

        public MigrationServiceTests()
        {
            TestBlocksContext.Set();
            _validator.Setup(v => v.ValidateAsync(It.IsAny<MigrationRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ValidationResult());
            _configuration.Setup(c => c["RootTenantId"]).Returns("root-tenant");
            _configuration.Setup(c => c["NotificationServiceUrl"]).Returns("https://notify.test/send");
            _cryptoService.Setup(c => c.Hash(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>())).Returns("hashed");
            _mailDriverService.Setup(m => m.SendAsync(It.IsAny<SendMail>()))
                .ReturnsAsync(new BaseMutationResponse { IsSuccess = true });
        }

        public void Dispose() => TestBlocksContext.Clear();

        private MigrationService CreateService() => new(
            _cacheClient.Object,
            _mailDriverService.Object,
            _messageClient.Object,
            _validator.Object,
            _migrationRepository.Object,
            _configuration.Object,
            _tenants.Object,
            _cryptoService.Object,
            _httpService.Object,
            _userRepository.Object,
            _logger.Object);

        private static MigrationRequest Request(params MigrationServiceNames[] services) => new()
        {
            ProjectKey = "SOURCE-1",
            TargetedProjectKey = "TARGET-1",
            TenantGroupId = "group-1",
            Services = [.. services.Select(s => new ServiceDetails { ServiceName = s, ShouldOverWriteExistingData = true })]
        };

        private static MigrationTracker Tracker() => new()
        {
            ItemId = "tracker-1",
            ProjectKey = "SOURCE-1",
            TargetedProjectKey = "TARGET-1",
            TenantGroupId = "group-1"
        };

        private void SetupNotificationPost(NotificationResponse? response)
        {
            _httpService.Setup(h => h.Post<NotificationResponse>(
                    It.IsAny<object>(), It.IsAny<string>(), It.IsAny<string>(),
                    It.IsAny<Dictionary<string, string>>(), It.IsAny<CancellationToken>(), It.IsAny<int?>()))
                .ReturnsAsync((response!, "raw"));
        }

        #region Migrate

        [Fact]
        public async Task Migrate_InvalidRequest_ReturnsValidationErrors()
        {
            _validator.Setup(v => v.ValidateAsync(It.IsAny<MigrationRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ValidationResult([new ValidationFailure("ProjectKey", "required")]));

            var result = await CreateService().Migrate(Request(MigrationServiceNames.IAM));

            result.IsSuccess.Should().BeFalse();
            result.Errors.Should().ContainKey("ProjectKey");
            _cacheClient.Verify(c => c.AddStringValueAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<long>()), Times.Never);
        }

        [Fact]
        public async Task Migrate_UserNotFound_ReturnsUserDetailsNotFound()
        {
            _userRepository.Setup(r => r.GetUserByIdAsync("user-123")).ReturnsAsync((User)null!);

            var result = await CreateService().Migrate(Request(MigrationServiceNames.IAM));

            result.IsSuccess.Should().BeFalse();
            result.Errors!["message"].Should().Be("user_details_not_found");
        }

        [Fact]
        public async Task Migrate_UserWithoutEmail_ReturnsUserDetailsNotFound()
        {
            _userRepository.Setup(r => r.GetUserByIdAsync("user-123")).ReturnsAsync(new User { ItemId = "user-123", Email = "" });

            var result = await CreateService().Migrate(Request(MigrationServiceNames.IAM));

            result.Errors!["message"].Should().Be("user_details_not_found");
        }

        [Fact]
        public async Task Migrate_ValidRequest_CachesOtpAndSendsMail()
        {
            _userRepository.Setup(r => r.GetUserByIdAsync("user-123"))
                .ReturnsAsync(new User { ItemId = "user-123", Email = "owner@example.com" });

            string? cachedPayload = null;
            _cacheClient.Setup(c => c.AddStringValueAsync(It.IsAny<string>(), It.IsAny<string>(), 600))
                .Callback<string, string, long>((_, value, _) => cachedPayload = value)
                .ReturnsAsync(true);

            var result = await CreateService().Migrate(Request(MigrationServiceNames.IAM));

            result.IsSuccess.Should().BeTrue();
            result.VerificationId.Should().NotBeNullOrWhiteSpace();
            cachedPayload.Should().NotBeNull();

            var cached = JsonSerializer.Deserialize<MigrationOtpData>(cachedPayload!);
            cached!.Code.Should().HaveLength(5);
            cached.Request.ProjectKey.Should().Be("SOURCE-1");

            _mailDriverService.Verify(m => m.SendAsync(It.Is<SendMail>(
                s => s.Purpose == "MfaViaEmail" && s.To.Contains("owner@example.com"))), Times.Once);
        }

        [Fact]
        public async Task Migrate_MailFails_ReturnsUnsuccessfulResponse()
        {
            _userRepository.Setup(r => r.GetUserByIdAsync("user-123"))
                .ReturnsAsync(new User { ItemId = "user-123", Email = "owner@example.com" });
            _mailDriverService.Setup(m => m.SendAsync(It.IsAny<SendMail>()))
                .ReturnsAsync(new BaseMutationResponse { IsSuccess = false });

            var result = await CreateService().Migrate(Request(MigrationServiceNames.IAM));

            result.IsSuccess.Should().BeFalse();
            result.VerificationId.Should().NotBeNullOrWhiteSpace();
        }

        [Fact]
        public async Task Migrate_RepositoryThrows_Rethrows()
        {
            _userRepository.Setup(r => r.GetUserByIdAsync("user-123")).ThrowsAsync(new InvalidOperationException("boom"));

            var act = async () => await CreateService().Migrate(Request(MigrationServiceNames.IAM));

            await act.Should().ThrowAsync<InvalidOperationException>();
        }

        [Fact]
        public void GenerateSecureRandomNumber_ReturnsFiveDigitNumberInRange()
        {
            for (var i = 0; i < 50; i++)
            {
                var value = MigrationService.GenerateSecureRandomNumber();
                value.Should().HaveLength(5);
                int.Parse(value).Should().BeInRange(11111, 99999);
            }
        }

        #endregion

        #region VerifyAsync

        [Fact]
        public async Task VerifyAsync_UnknownVerificationId_ReturnsInvalidTwoFactorId()
        {
            _cacheClient.Setup(c => c.KeyExistsAsync("vid")).ReturnsAsync(false);

            var result = await CreateService().VerifyAsync(new MigrationVerifyOtpRequest { VerificationId = "vid" });

            result.IsValid.Should().BeFalse();
            result.Errors!["message"].Should().Be("invalid_two_factor_id");
        }

        [Fact]
        public async Task VerifyAsync_EmptyCachedValue_ReturnsInvalidTwoFactorId()
        {
            _cacheClient.Setup(c => c.KeyExistsAsync("vid")).ReturnsAsync(true);
            _cacheClient.Setup(c => c.GetStringValueAsync("vid")).ReturnsAsync(string.Empty);

            var result = await CreateService().VerifyAsync(new MigrationVerifyOtpRequest { VerificationId = "vid" });

            result.Errors!["message"].Should().Be("invalid_two_factor_id");
        }

        [Fact]
        public async Task VerifyAsync_NullDeserializedData_ReturnsInvalidTwoFactorId()
        {
            _cacheClient.Setup(c => c.KeyExistsAsync("vid")).ReturnsAsync(true);
            _cacheClient.Setup(c => c.GetStringValueAsync("vid")).ReturnsAsync("null");

            var result = await CreateService().VerifyAsync(new MigrationVerifyOtpRequest { VerificationId = "vid" });

            result.Errors!["message"].Should().Be("invalid_two_factor_id");
        }

        [Fact]
        public async Task VerifyAsync_WrongCode_ReturnsInvalidTwoFactorCode()
        {
            _cacheClient.Setup(c => c.KeyExistsAsync("vid")).ReturnsAsync(true);
            _cacheClient.Setup(c => c.GetStringValueAsync("vid"))
                .ReturnsAsync(JsonSerializer.Serialize(new MigrationOtpData
                {
                    Code = "11111",
                    Request = Request(MigrationServiceNames.IAM)
                }));

            var result = await CreateService().VerifyAsync(new MigrationVerifyOtpRequest
            {
                VerificationId = "vid",
                VerificationCode = "22222"
            });

            result.IsValid.Should().BeFalse();
            result.Errors!["message"].Should().Be("invalid_two_factor_code");
            _migrationRepository.Verify(r => r.CreateMigrationTrackerAsync(It.IsAny<MigrationTracker>()), Times.Never);
        }

        [Fact]
        public async Task VerifyAsync_CorrectCode_CreatesTrackerAndDispatchesQueuedServices()
        {
            _cacheClient.Setup(c => c.KeyExistsAsync("vid")).ReturnsAsync(true);
            _cacheClient.Setup(c => c.GetStringValueAsync("vid"))
                .ReturnsAsync(JsonSerializer.Serialize(new MigrationOtpData
                {
                    Code = "11111",
                    Request = Request(MigrationServiceNames.IAM, MigrationServiceNames.Language,
                        MigrationServiceNames.Email, MigrationServiceNames.DataGateway, MigrationServiceNames.MFA)
                }));

            MigrationTracker? tracker = null;
            _migrationRepository.Setup(r => r.CreateMigrationTrackerAsync(It.IsAny<MigrationTracker>()))
                .Callback<MigrationTracker>(t => tracker = t)
                .ReturnsAsync("tracker-1");
            SetupNotificationPost(new NotificationResponse { isSuccess = true });

            var result = await CreateService().VerifyAsync(new MigrationVerifyOtpRequest
            {
                VerificationId = "vid",
                VerificationCode = "11111"
            });

            result.IsSuccess.Should().BeTrue();
            result.IsValid.Should().BeTrue();
            _cacheClient.Verify(c => c.RemoveKeyAsync("vid"), Times.Once);

            tracker.Should().NotBeNull();
            tracker!.IAM!.QueueName.Should().Be(IdentifierConstants.IamQueue);
            tracker.LanguageService!.QueueName.Should().Be(IdentifierConstants.LanguageDataMigrationQueue);
            tracker.Email!.QueueName.Should().Be(IdentifierConstants.GenericMigrationQueue);
            tracker.DataGateway!.QueueName.Should().Be(IdentifierConstants.GenericMigrationQueue);
            tracker.MFA!.QueueName.Should().BeEmpty();

            // MFA has no queue, so only the four routable services get an event.
            _messageClient.Verify(m => m.SendToConsumerAsync(
                It.IsAny<ConsumerMessage<EnvironmentDataMigrationEvent>>()), Times.Exactly(4));
        }

        [Fact]
        public async Task VerifyAsync_CorrectCode_MapsEveryTrackedService()
        {
            _cacheClient.Setup(c => c.KeyExistsAsync("vid")).ReturnsAsync(true);
            _cacheClient.Setup(c => c.GetStringValueAsync("vid"))
                .ReturnsAsync(JsonSerializer.Serialize(new MigrationOtpData
                {
                    Code = "11111",
                    Request = Request(MigrationServiceNames.Authentication, MigrationServiceNames.CAPTCHA,
                        MigrationServiceNames.Notifications, MigrationServiceNames.Storage)
                }));

            MigrationTracker? tracker = null;
            _migrationRepository.Setup(r => r.CreateMigrationTrackerAsync(It.IsAny<MigrationTracker>()))
                .Callback<MigrationTracker>(t => tracker = t)
                .ReturnsAsync("tracker-1");
            SetupNotificationPost(new NotificationResponse { isSuccess = true });

            await CreateService().VerifyAsync(new MigrationVerifyOtpRequest
            {
                VerificationId = "vid",
                VerificationCode = "11111"
            });

            tracker!.Authentication.Should().NotBeNull();
            tracker.CAPTCHA.Should().NotBeNull();
            tracker.Notifications.Should().NotBeNull();
            tracker.Storage.Should().NotBeNull();
            tracker.CreatedBy.Should().Be("user-123");
            _messageClient.Verify(m => m.SendToConsumerAsync(
                It.IsAny<ConsumerMessage<EnvironmentDataMigrationEvent>>()), Times.Never);
        }

        #endregion

        #region Notifications

        [Fact]
        public async Task NotifyDataMigrationProgress_PostsWithBlocksHeaders()
        {
            _tenants.Setup(t => t.GetTenantByID("root-tenant")).Returns(new Tenant
            {
                TenantId = "root-tenant",
                TenantSalt = "salty",
                DbConnectionString = "mongodb://localhost",
                JwtTokenParameters = new JwtTokenParameters { PrivateCertificatePassword = "pw", IssueDate = DateTime.UtcNow }
            });
            Dictionary<string, string>? headers = null;
            _httpService.Setup(h => h.Post<NotificationResponse>(
                    It.IsAny<object>(), "https://notify.test/send", "application/json",
                    It.IsAny<Dictionary<string, string>>(), It.IsAny<CancellationToken>(), It.IsAny<int?>()))
                .Callback((object _, string _, string _, Dictionary<string, string>? h, CancellationToken _, int? _) => headers = h)
                .ReturnsAsync((new NotificationResponse { isSuccess = true }, "raw"));

            var result = await CreateService().NotifyDataMigrationProgress(true, "SOURCE-1", "TARGET-1");

            result.Should().BeTrue();
            headers.Should().NotBeNull();
            headers!["x-blocks-key"].Should().Be("root-tenant");
            headers["Secret"].Should().Be("hashed");
            _cryptoService.Verify(c => c.Hash("root-tenant", "salty", false), Times.Once);
        }

        [Fact]
        public async Task NotifyDataMigrationProgress_NullResponse_ReturnsFalse()
        {
            SetupNotificationPost(null);

            var result = await CreateService().NotifyDataMigrationProgress(false, "SOURCE-1", "TARGET-1");

            result.Should().BeFalse();
        }

        [Fact]
        public async Task NotifyEnvironmentDataMigration_ReturnsResponseFlag()
        {
            SetupNotificationPost(new NotificationResponse { isSuccess = true });

            var result = await CreateService().NotifyEnvironmentDataMigration(false, "SOURCE-1", "TARGET-1");

            result.Should().BeTrue();
        }

        [Fact]
        public async Task NotifyEnvironmentDataMigration_NullResponse_ReturnsFalse()
        {
            SetupNotificationPost(null);

            var result = await CreateService().NotifyEnvironmentDataMigration(true, "SOURCE-1", "TARGET-1");

            result.Should().BeFalse();
        }

        [Fact]
        public async Task NotifyServiceDataMigrationProgress_DelegatesToDataMigrationProgress()
        {
            SetupNotificationPost(new NotificationResponse { isSuccess = true });

            var result = await CreateService().NotifyServiceDataMigrationProgress(true, "SOURCE-1", "TARGET-1");

            result.Should().BeTrue();
            _httpService.Verify(h => h.Post<NotificationResponse>(
                It.IsAny<object>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<Dictionary<string, string>>(), It.IsAny<CancellationToken>(), It.IsAny<int?>()), Times.Once);
        }

        [Fact]
        public async Task NotifyDataMigrationEvent_DelegatesToEnvironmentDataMigration()
        {
            SetupNotificationPost(new NotificationResponse { isSuccess = false });

            var result = await CreateService().NotifyDataMigrationEvent(true, "SOURCE-1", "TARGET-1");

            result.Should().BeFalse();
        }

        [Fact]
        public async Task NotifyMigrationStarted_NullResponse_ReturnsFalse()
        {
            SetupNotificationPost(null);

            var result = await CreateService().NotifyMigrationStarted("SOURCE-1", "TARGET-1");

            result.Should().BeFalse();
        }

        #endregion

        #region AreAllServicesCompleted

        [Fact]
        public void AreAllServicesCompleted_NoTrackedServices_ReturnsFalse()
        {
            CreateService().AreAllServicesCompleted(Tracker()).Should().BeFalse();
        }

        [Fact]
        public void AreAllServicesCompleted_OneIncomplete_ReturnsFalse()
        {
            var tracker = Tracker();
            tracker.IAM = new ServiceMigrationStatus { IsCompleted = true };
            tracker.Email = new ServiceMigrationStatus { IsCompleted = false };

            CreateService().AreAllServicesCompleted(tracker).Should().BeFalse();
        }

        [Fact]
        public void AreAllServicesCompleted_AllComplete_ReturnsTrue()
        {
            var tracker = Tracker();
            tracker.IAM = new ServiceMigrationStatus { IsCompleted = true };
            tracker.LanguageService = new ServiceMigrationStatus { IsCompleted = true };

            CreateService().AreAllServicesCompleted(tracker).Should().BeTrue();
        }

        #endregion

        #region MigrateEnvironmentDataAsync

        [Fact]
        public async Task MigrateEnvironmentDataAsync_TrackerNotFound_DoesNothing()
        {
            _migrationRepository.Setup(r => r.GetMigrationTrackerAsync("tracker-1")).ReturnsAsync((MigrationTracker)null!);

            await CreateService().MigrateEnvironmentDataAsync("SOURCE-1", "TARGET-1", true, "tracker-1");

            _migrationRepository.Verify(r => r.MigrateCollectionAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>()), Times.Never);
        }

        [Fact]
        public async Task MigrateEnvironmentDataAsync_NoGenericQueueServices_DoesNothing()
        {
            var tracker = Tracker();
            tracker.IAM = new ServiceMigrationStatus { IsCompleted = false };
            tracker.DataGateway = new ServiceMigrationStatus { IsCompleted = true };
            _migrationRepository.Setup(r => r.GetMigrationTrackerAsync("tracker-1")).ReturnsAsync(tracker);

            await CreateService().MigrateEnvironmentDataAsync("SOURCE-1", "TARGET-1", true, "tracker-1");

            _migrationRepository.Verify(r => r.MigrateCollectionAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>()), Times.Never);
        }

        [Fact]
        public async Task MigrateEnvironmentDataAsync_IncompleteGenericServices_MigratesEachCollection()
        {
            var tracker = Tracker();
            tracker.DataGateway = new ServiceMigrationStatus { IsCompleted = false };
            tracker.Email = new ServiceMigrationStatus { IsCompleted = false };
            _migrationRepository.Setup(r => r.GetMigrationTrackerAsync("tracker-1")).ReturnsAsync(tracker);
            _migrationRepository.Setup(r => r.MigrateCollectionAsync(
                    "SOURCE-1", "TARGET-1", It.IsAny<string>(), true))
                .ReturnsAsync((10, 10));

            await CreateService().MigrateEnvironmentDataAsync("SOURCE-1", "TARGET-1", true, "tracker-1");

            // DataGateway needs four collections, Email one.
            _migrationRepository.Verify(r => r.MigrateCollectionAsync(
                "SOURCE-1", "TARGET-1", It.IsAny<string>(), true), Times.Exactly(5));
            _messageClient.Verify(m => m.SendToConsumerAsync(
                It.Is<ConsumerMessage<MigrationCompletionEvent>>(c => c.Payload.IsSuccess)), Times.Exactly(2));
        }

        [Fact]
        public async Task MigrateEnvironmentDataAsync_CollectionMigrationFails_SendsFailureCompletionEvent()
        {
            var tracker = Tracker();
            tracker.Email = new ServiceMigrationStatus { IsCompleted = false };
            _migrationRepository.Setup(r => r.GetMigrationTrackerAsync("tracker-1")).ReturnsAsync(tracker);
            _migrationRepository.Setup(r => r.MigrateCollectionAsync(
                    It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>()))
                .ThrowsAsync(new InvalidOperationException("copy failed"));

            await CreateService().MigrateEnvironmentDataAsync("SOURCE-1", "TARGET-1", false, "tracker-1");

            // One failure event from the inner handler, one from the outer catch.
            _messageClient.Verify(m => m.SendToConsumerAsync(
                It.Is<ConsumerMessage<MigrationCompletionEvent>>(
                    c => !c.Payload.IsSuccess && c.Payload.ErrorMessage == "copy failed")), Times.Exactly(2));
        }

        #endregion

        #region DataCleanupAsync

        [Fact]
        public async Task DataCleanupAsync_NullRequest_ReturnsFalse()
        {
            var result = await CreateService().DataCleanupAsync(null!);

            result.Should().BeFalse();
        }

        [Fact]
        public async Task DataCleanupAsync_BlankProjectKey_ReturnsFalse()
        {
            var result = await CreateService().DataCleanupAsync(new DataCleanupRequest { ProjectKey = "  " });

            result.Should().BeFalse();
            _migrationRepository.Verify(r => r.CleanupCollectionAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task DataCleanupAsync_ValidRequest_CleansAndMigratesLocalizationCollections()
        {
            _migrationRepository.Setup(r => r.CleanupCollectionAsync("TARGET-1", It.IsAny<string>())).ReturnsAsync(true);
            _migrationRepository.Setup(r => r.MigrateDocumentsAsync("TARGET-1", It.IsAny<string>())).ReturnsAsync(true);

            var result = await CreateService().DataCleanupAsync(new DataCleanupRequest { ProjectKey = "TARGET-1" });

            result.Should().BeTrue();
            _migrationRepository.Verify(r => r.CleanupCollectionAsync("TARGET-1", It.IsAny<string>()), Times.Exactly(4));
            _migrationRepository.Verify(r => r.MigrateDocumentsAsync("TARGET-1", It.IsAny<string>()), Times.Exactly(4));
        }

        [Fact]
        public async Task DataCleanupAsync_RepositoryThrows_ReturnsFalse()
        {
            _migrationRepository.Setup(r => r.CleanupCollectionAsync(It.IsAny<string>(), It.IsAny<string>()))
                .ThrowsAsync(new InvalidOperationException("cleanup failed"));

            var result = await CreateService().DataCleanupAsync(new DataCleanupRequest { ProjectKey = "TARGET-1" });

            result.Should().BeFalse();
        }

        #endregion
    }
}
