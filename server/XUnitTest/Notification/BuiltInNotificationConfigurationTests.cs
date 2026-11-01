using System.Linq.Expressions;
using Blocks.Genesis;
using Blocks.Genesis.Entities.Logic;
using Blocks.Genesis.Entities.Os;
using CloudConfiguration.DomainService.Mail.RequestModel;
using CloudConfiguration.DomainService.Notification.RequestModel;
using DataAccess.InternalService;
using DomainService.Entities;
using DomainService.Notification;
using DomainService.Shared;
using FluentAssertions;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using Moq;
using XUnitTest.TestHelpers;
using CloudConfigurationRepository = CloudConfiguration.DomainService.Shared.Services.IConfigurationRepository;
using CloudConfigurationService = CloudConfiguration.DomainService.Shared.Services.ConfigurationService;
using NotifierConfigurationRepository = DomainService.Configuration.Services.IConfigurationRepository;
using NotifierConfigurationService = DomainService.Configuration.Services.ConfigurationService;
using NotifierDeleteRequest = DomainService.Configuration.DeleteConfigurationRequest;
using NotifierSaveRequest = DomainService.Configuration.SaveConfigurationRequest;

namespace XUnitTest.Notification
{
    internal static class BuiltInFixtures
    {
        public const string RootTenantId = "root-tenant";
        public const string ProjectTenantId = "project-tenant";

        public static Mock<ITenants> Tenants()
        {
            var tenants = new Mock<ITenants>();
            tenants.Setup(t => t.GetTenantByID(RootTenantId)).Returns(Tenant(RootTenantId, isRoot: true));
            tenants.Setup(t => t.GetTenantByID(ProjectTenantId)).Returns(Tenant(ProjectTenantId, isRoot: false));
            return tenants;
        }

        private static Tenant Tenant(string id, bool isRoot) => new()
        {
            TenantId = id,
            IsRootTenant = isRoot,
            DbConnectionString = "unused",
            JwtTokenParameters = new JwtTokenParameters
            {
                PublicCertificatePassword = string.Empty,
                PrivateCertificatePassword = string.Empty,
                IssueDate = DateTime.UtcNow,
            },
        };

        public static NotificationConfiguration BuiltIn(NotificationKind kind, bool isDisabled = false, string name = "data-import-failed") => new()
        {
            ItemId = "item-" + name,
            Name = name,
            ChannelToNotify = NotifierTypes.SignalR,
            NotificationType = NotificationReceiverTypes.UserSpecificReceiverType,
            NotifyMethod = "ReceiveNotification",
            EnablePersistence = true,
            IsBuiltIn = true,
            IsDisabled = isDisabled,
            Kind = kind,
            Importance = NotificationImportance.High,
            Description = "Tells the user an import failed.",
            SourceService = "blocks-data",
            ConsumerAppRecommendation = "Show a toast.",
            ConsumerAppNote = "Subscribe to the method.",
            CreatedDate = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            CreatedBy = "seed",
        };

        public static NotificationConfiguration Custom(bool isDisabled = false, string name = "my-app-event") => new()
        {
            ItemId = "item-" + name,
            Name = name,
            ChannelToNotify = NotifierTypes.SignalR,
            NotificationType = NotificationReceiverTypes.UserSpecificReceiverType,
            NotifyMethod = "ReceiveNotification",
            EnablePersistence = true,
            IsDisabled = isDisabled,
        };

        /// <summary>A save request that re-sends <paramref name="stored"/> unchanged, as the portal does.</summary>
        public static SaveNotificatonConfigurationRequest Resend(NotificationConfiguration stored) => new()
        {
            Name = stored.Name,
            ChannelToNotify = stored.ChannelToNotify,
            NotificationType = stored.NotificationType,
            NotifyMethod = stored.NotifyMethod,
            EnablePersistence = stored.EnablePersistence,
            IsUpdateRequest = true,
            IsBuiltIn = stored.IsBuiltIn,
            IsDisabled = stored.IsDisabled,
            Description = stored.Description,
            Importance = stored.Importance,
            Kind = stored.Kind,
            SourceService = stored.SourceService,
            ConsumerAppRecommendation = stored.ConsumerAppRecommendation,
            ConsumerAppNote = stored.ConsumerAppNote,
        };
    }

    /// <summary>The shared save/delete rules, without any service around them.</summary>
    public class NotificationConfigurationWriteRulesTests : IDisposable
    {
        public void Dispose()
        {
            TestBlocksContext.Clear();
            GC.SuppressFinalize(this);
        }

        private static NotificationConfigurationWrite Write(NotificationConfiguration from, bool? isBuiltIn = null, bool? isDisabled = null,
            string? notifyMethod = null, NotificationKind? kind = null) => new()
        {
            Name = from.Name,
            ChannelToNotify = from.ChannelToNotify,
            NotificationType = from.NotificationType,
            NotifyMethod = notifyMethod ?? from.NotifyMethod,
            EnablePersistence = from.EnablePersistence,
            IsBuiltIn = isBuiltIn,
            IsDisabled = isDisabled,
            Kind = kind,
        };

        [Fact]
        public void IsCallerRootTenant_IsFalseWithoutAContext()
        {
            TestBlocksContext.Clear();
            NotificationConfigurationWriteRules.IsCallerRootTenant(BuiltInFixtures.Tenants().Object).Should().BeFalse();
        }

        [Fact]
        public void IsCallerRootTenant_IsFalseForAnUnknownTenant()
        {
            TestBlocksContext.Set("tenant-nobody-knows");
            NotificationConfigurationWriteRules.IsCallerRootTenant(BuiltInFixtures.Tenants().Object).Should().BeFalse();
        }

        [Fact]
        public void IsCallerRootTenant_IsTrueOnlyForTheRootTenant()
        {
            var tenants = BuiltInFixtures.Tenants().Object;
            TestBlocksContext.Set(BuiltInFixtures.RootTenantId);
            NotificationConfigurationWriteRules.IsCallerRootTenant(tenants).Should().BeTrue();
            TestBlocksContext.Set(BuiltInFixtures.ProjectTenantId);
            NotificationConfigurationWriteRules.IsCallerRootTenant(tenants).Should().BeFalse();
        }

        [Fact]
        public void Refusal_LeavesTheTargetUnchanged()
        {
            var stored = BuiltInFixtures.BuiltIn(NotificationKind.Alert);
            var before = stored.ToBsonDocument();

            var refusal = NotificationConfigurationWriteRules.Apply(stored, false, Write(stored, notifyMethod: "Other", isDisabled: true), callerIsRoot: false);

            refusal.Should().NotBeNull();
            stored.ToBsonDocument().Should().BeEquivalentTo(before);
        }

        [Fact]
        public void RootMayConvertABuiltInToCustom()
        {
            var stored = BuiltInFixtures.BuiltIn(NotificationKind.Job);

            NotificationConfigurationWriteRules.Apply(stored, false, Write(stored, isBuiltIn: false), callerIsRoot: true).Should().BeNull();
            stored.IsBuiltIn.Should().BeFalse();
        }

        [Fact]
        public void DisablingWhenTheRootAlsoChangesKindToJob_IsRefused()
        {
            var stored = BuiltInFixtures.BuiltIn(NotificationKind.Alert);

            NotificationConfigurationWriteRules.Apply(stored, false, Write(stored, isDisabled: true, kind: NotificationKind.Job), callerIsRoot: true)
                .Should().Be((nameof(NotificationConfiguration.IsDisabled), NotificationConfigurationWriteRules.CannotBeDisabled));
        }

        [Fact]
        public void EnablingAJobBuiltIn_IsAllowedForEveryTenant()
        {
            var stored = BuiltInFixtures.BuiltIn(NotificationKind.Job, isDisabled: true);

            NotificationConfigurationWriteRules.Apply(stored, false, Write(stored, isDisabled: false), callerIsRoot: false).Should().BeNull();
            stored.IsDisabled.Should().BeFalse();
        }

        [Fact]
        public void CheckDelete_UnknownIdPassesThrough() =>
            NotificationConfigurationWriteRules.CheckDelete(null, callerIsRoot: false).Should().BeNull();
    }

    /// <summary>Save and delete rules on the CloudConfiguration service (no API route today; same rules as blocks-os).</summary>
    public class CloudConfigurationBuiltInRulesTests : IDisposable
    {
        private readonly Mock<CloudConfigurationRepository> _repository = new();
        private readonly CloudConfigurationService _sut;
        private NotificationConfiguration? _saved;

        public CloudConfigurationBuiltInRulesTests()
        {
            var validator = new Mock<IValidator<SaveNotificatonConfigurationRequest>>();
            validator.Setup(v => v.ValidateAsync(It.IsAny<SaveNotificatonConfigurationRequest>(), It.IsAny<CancellationToken>()))
                     .ReturnsAsync(new ValidationResult());
            _repository.Setup(r => r.SaveNotificationConfigurationAsync(It.IsAny<NotificationConfiguration>()))
                       .Callback<NotificationConfiguration>(c => _saved = c).Returns(Task.CompletedTask);
            _repository.Setup(r => r.DeleteNotificationConfigurationAsync(It.IsAny<DeleteNotificatoinConfigurationRequest>()))
                       .ReturnsAsync(new BaseResponse { IsSuccess = true });

            _sut = new CloudConfigurationService(_repository.Object, validator.Object,
                Mock.Of<IValidator<MailConfiguration>>(), NullLogger<CloudConfigurationService>.Instance, BuiltInFixtures.Tenants().Object);
        }

        public void Dispose()
        {
            TestBlocksContext.Clear();
            GC.SuppressFinalize(this);
        }

        private static void AsRoot() => TestBlocksContext.Set(BuiltInFixtures.RootTenantId, "root-user");
        private static void AsProject() => TestBlocksContext.Set(BuiltInFixtures.ProjectTenantId, "project-user");

        private void Stored(NotificationConfiguration configuration)
        {
            _repository.Setup(r => r.GetNotificationConfigurationByNameAsync(configuration.Name)).ReturnsAsync(configuration);
            _repository.Setup(r => r.GetNotificationConfigurationByIdAsync(configuration.ItemId)).ReturnsAsync(configuration);
        }

        private void NothingSaved() =>
            _repository.Verify(r => r.SaveNotificationConfigurationAsync(It.IsAny<NotificationConfiguration>()), Times.Never);

        private static void Refused(BaseResponse response, string field, string error)
        {
            response.IsSuccess.Should().BeFalse();
            response.Errors.Should().ContainKey(field).WhoseValue.Should().Be(error);
        }

        // Rule 1
        [Fact]
        public async Task NewBuiltIn_ByProjectTenant_IsRefused()
        {
            AsProject();
            var request = BuiltInFixtures.Resend(BuiltInFixtures.BuiltIn(NotificationKind.Alert, name: "brand-new"));
            request.IsUpdateRequest = false;

            Refused(await _sut.SaveNotificationConfigurationAsync(request), "IsBuiltIn", NotificationConfigurationWriteRules.OnlyRootTenantCanManageBuiltIn);
            NothingSaved();
        }

        [Fact]
        public async Task NewBuiltIn_ByRoot_IsStoredWithAllFields()
        {
            AsRoot();
            var request = BuiltInFixtures.Resend(BuiltInFixtures.BuiltIn(NotificationKind.Alert, name: "brand-new"));
            request.IsUpdateRequest = false;

            (await _sut.SaveNotificationConfigurationAsync(request)).IsSuccess.Should().BeTrue();
            _saved!.IsBuiltIn.Should().BeTrue();
            _saved.Kind.Should().Be(NotificationKind.Alert);
            _saved.Importance.Should().Be(NotificationImportance.High);
            _saved.SourceService.Should().Be("blocks-data");
            _saved.ItemId.Should().NotBeNullOrEmpty();
            _saved.CreatedBy.Should().Be("root-user");
        }

        // Rule 2
        [Fact]
        public async Task ExistingBuiltIn_RootChangesFields_IsBuiltInKeptWhenOmitted()
        {
            AsRoot();
            var stored = BuiltInFixtures.BuiltIn(NotificationKind.Alert);
            Stored(stored);
            var request = BuiltInFixtures.Resend(stored);
            request.IsBuiltIn = null;
            request.NotifyMethod = "ReceiveAlert";
            request.Description = "New text.";

            (await _sut.SaveNotificationConfigurationAsync(request)).IsSuccess.Should().BeTrue();
            _saved!.IsBuiltIn.Should().BeTrue();
            _saved.NotifyMethod.Should().Be("ReceiveAlert");
            _saved.Description.Should().Be("New text.");
            _saved.ItemId.Should().Be(stored.ItemId);
            _saved.CreatedBy.Should().Be("seed");
        }

        // Rule 3
        [Fact]
        public async Task ExistingBuiltIn_ProjectSwitchesOnlyIsDisabled_IsSavedAndMetadataKept()
        {
            AsProject();
            var stored = BuiltInFixtures.BuiltIn(NotificationKind.Alert);
            Stored(stored);
            var request = BuiltInFixtures.Resend(stored);
            request.IsDisabled = true;

            (await _sut.SaveNotificationConfigurationAsync(request)).IsSuccess.Should().BeTrue();
            _saved!.IsDisabled.Should().BeTrue();
            _saved.IsBuiltIn.Should().BeTrue();
            _saved.Description.Should().Be("Tells the user an import failed.");
            _saved.LastUpdatedBy.Should().Be("project-user");
        }

        [Fact]
        public async Task ExistingBuiltIn_ProjectSendsOnlyTheOriginalFieldsPlusIsDisabled_IsSaved()
        {
            AsProject();
            var stored = BuiltInFixtures.BuiltIn(NotificationKind.Security);
            Stored(stored);
            var request = new SaveNotificatonConfigurationRequest
            {
                Name = stored.Name, ChannelToNotify = stored.ChannelToNotify, NotificationType = stored.NotificationType,
                NotifyMethod = stored.NotifyMethod, EnablePersistence = stored.EnablePersistence, IsUpdateRequest = true, IsDisabled = true,
            };

            (await _sut.SaveNotificationConfigurationAsync(request)).IsSuccess.Should().BeTrue();
            _saved!.IsDisabled.Should().BeTrue();
            _saved.Kind.Should().Be(NotificationKind.Security);
        }

        [Theory]
        [InlineData("NotifyMethod")]
        [InlineData("EnablePersistence")]
        [InlineData("ChannelToNotify")]
        [InlineData("NotificationType")]
        [InlineData("Description")]
        [InlineData("Importance")]
        [InlineData("Kind")]
        [InlineData("SourceService")]
        [InlineData("ConsumerAppRecommendation")]
        [InlineData("ConsumerAppNote")]
        [InlineData("IsBuiltIn")]
        public async Task ExistingBuiltIn_ProjectChangesAnotherField_IsRefused(string field)
        {
            AsProject();
            var stored = BuiltInFixtures.BuiltIn(NotificationKind.Alert);
            Stored(stored);
            var request = BuiltInFixtures.Resend(stored);
            switch (field)
            {
                case "NotifyMethod": request.NotifyMethod = "Other"; break;
                case "EnablePersistence": request.EnablePersistence = false; break;
                case "ChannelToNotify": request.ChannelToNotify = NotifierTypes.Firebase; break;
                case "NotificationType": request.NotificationType = NotificationReceiverTypes.BroadcastReceiverType; break;
                case "Description": request.Description = "changed"; break;
                case "Importance": request.Importance = NotificationImportance.Low; break;
                case "Kind": request.Kind = NotificationKind.Access; break;
                case "SourceService": request.SourceService = "blocks-iam"; break;
                case "ConsumerAppRecommendation": request.ConsumerAppRecommendation = "changed"; break;
                case "ConsumerAppNote": request.ConsumerAppNote = "changed"; break;
                case "IsBuiltIn": request.IsBuiltIn = false; break;
            }

            Refused(await _sut.SaveNotificationConfigurationAsync(request), field, NotificationConfigurationWriteRules.BuiltInIsReadOnly);
            NothingSaved();
        }

        // Rule 4
        [Theory]
        [InlineData(NotificationKind.Job, true)]
        [InlineData(NotificationKind.Progress, true)]
        [InlineData(NotificationKind.Job, false)]
        public async Task DisablingABuiltInJobOrProgress_IsRefusedForEveryTenant(NotificationKind kind, bool asRoot)
        {
            if (asRoot) AsRoot(); else AsProject();
            var stored = BuiltInFixtures.BuiltIn(kind);
            Stored(stored);
            var request = BuiltInFixtures.Resend(stored);
            request.IsDisabled = true;

            Refused(await _sut.SaveNotificationConfigurationAsync(request), "IsDisabled", NotificationConfigurationWriteRules.CannotBeDisabled);
            NothingSaved();
        }

        // Rule 5
        [Fact]
        public async Task Custom_ProjectSetsIsBuiltIn_IsRefused()
        {
            AsProject();
            var stored = BuiltInFixtures.Custom();
            Stored(stored);
            var request = BuiltInFixtures.Resend(stored);
            request.IsBuiltIn = true;

            Refused(await _sut.SaveNotificationConfigurationAsync(request), "IsBuiltIn", NotificationConfigurationWriteRules.OnlyRootTenantCanManageBuiltIn);
            NothingSaved();
        }

        [Fact]
        public async Task Custom_ProjectDisablesAndChangesFields_IsSaved()
        {
            AsProject();
            var stored = BuiltInFixtures.Custom();
            Stored(stored);
            var request = BuiltInFixtures.Resend(stored);
            request.NotifyMethod = "Mine";
            request.IsDisabled = true;
            request.Kind = NotificationKind.Job;

            (await _sut.SaveNotificationConfigurationAsync(request)).IsSuccess.Should().BeTrue();
            _saved!.IsDisabled.Should().BeTrue();
            _saved.IsBuiltIn.Should().BeFalse();
            _saved.NotifyMethod.Should().Be("Mine");
        }

        // Rule 6
        [Fact]
        public async Task OldClient_NewCustom_IsStoredExactlyAsBefore()
        {
            AsProject();
            var request = new SaveNotificatonConfigurationRequest
            {
                Name = "old-client", ChannelToNotify = NotifierTypes.SignalR, NotificationType = NotificationReceiverTypes.BroadcastReceiverType,
                NotifyMethod = "ReceiveNotification", EnablePersistence = true,
            };

            (await _sut.SaveNotificationConfigurationAsync(request)).IsSuccess.Should().BeTrue();
            var bson = _saved!.ToBsonDocument();
            foreach (var field in new[] { "IsBuiltIn", "IsDisabled", "Description", "Importance", "Kind", "SourceService", "ConsumerAppRecommendation", "ConsumerAppNote" })
            {
                bson.Contains(field).Should().BeFalse($"{field} has its default value and must not be written");
            }
        }

        [Fact]
        public async Task OldClient_UpdatesADisabledCustom_KeepsIsDisabled()
        {
            AsProject();
            var stored = BuiltInFixtures.Custom(isDisabled: true);
            Stored(stored);
            var request = new SaveNotificatonConfigurationRequest
            {
                Name = stored.Name, ChannelToNotify = stored.ChannelToNotify, NotificationType = stored.NotificationType,
                NotifyMethod = "Renamed", EnablePersistence = stored.EnablePersistence, IsUpdateRequest = true,
            };

            (await _sut.SaveNotificationConfigurationAsync(request)).IsSuccess.Should().BeTrue();
            _saved!.IsDisabled.Should().BeTrue();
            _saved.NotifyMethod.Should().Be("Renamed");
        }

        [Fact]
        public async Task NoContext_IsTreatedAsNotRoot()
        {
            TestBlocksContext.Clear();
            var request = BuiltInFixtures.Resend(BuiltInFixtures.BuiltIn(NotificationKind.Alert, name: "brand-new"));

            Refused(await _sut.SaveNotificationConfigurationAsync(request), "IsBuiltIn", NotificationConfigurationWriteRules.OnlyRootTenantCanManageBuiltIn);
        }

        // Delete
        [Fact]
        public async Task DeleteBuiltIn_ByProject_IsRefused()
        {
            AsProject();
            var stored = BuiltInFixtures.BuiltIn(NotificationKind.Alert);
            Stored(stored);

            Refused(await _sut.DeleteNotificationConfigurationAsync(new DeleteNotificatoinConfigurationRequest { ItemId = stored.ItemId }),
                "ItemId", NotificationConfigurationWriteRules.OnlyRootTenantCanManageBuiltIn);
            _repository.Verify(r => r.DeleteNotificationConfigurationAsync(It.IsAny<DeleteNotificatoinConfigurationRequest>()), Times.Never);
        }

        [Fact]
        public async Task DeleteBuiltIn_ByRoot_IsDeleted()
        {
            AsRoot();
            var stored = BuiltInFixtures.BuiltIn(NotificationKind.Alert);
            Stored(stored);

            (await _sut.DeleteNotificationConfigurationAsync(new DeleteNotificatoinConfigurationRequest { ItemId = stored.ItemId })).IsSuccess.Should().BeTrue();
            _repository.Verify(r => r.DeleteNotificationConfigurationAsync(It.Is<DeleteNotificatoinConfigurationRequest>(d => d.ItemId == stored.ItemId)), Times.Once);
        }

        [Fact]
        public async Task DeleteCustom_ByProject_IsDeletedAsToday()
        {
            AsProject();
            var stored = BuiltInFixtures.Custom();
            Stored(stored);

            (await _sut.DeleteNotificationConfigurationAsync(new DeleteNotificatoinConfigurationRequest { ItemId = stored.ItemId })).IsSuccess.Should().BeTrue();
            _repository.Verify(r => r.DeleteNotificationConfigurationAsync(It.IsAny<DeleteNotificatoinConfigurationRequest>()), Times.Once);
        }

        [Fact]
        public async Task DeleteUnknownId_PassesThroughAsToday()
        {
            AsProject();

            (await _sut.DeleteNotificationConfigurationAsync(new DeleteNotificatoinConfigurationRequest { ItemId = "nope" })).IsSuccess.Should().BeTrue();
            _repository.Verify(r => r.DeleteNotificationConfigurationAsync(It.IsAny<DeleteNotificatoinConfigurationRequest>()), Times.Once);
        }
    }

    /// <summary>The Notification-side configuration service applies the same rules (one shared rule class).</summary>
    public class NotifierConfigurationBuiltInRulesTests : IDisposable
    {
        private readonly Mock<NotifierConfigurationRepository> _repository = new();
        private readonly NotifierConfigurationService _sut;

        public NotifierConfigurationBuiltInRulesTests()
        {
            var validator = new Mock<IValidator<NotifierSaveRequest>>();
            validator.Setup(v => v.ValidateAsync(It.IsAny<NotifierSaveRequest>(), It.IsAny<CancellationToken>())).ReturnsAsync(new ValidationResult());
            _repository.Setup(r => r.DeleteConfigurationAsync(It.IsAny<NotifierDeleteRequest>())).ReturnsAsync(new BaseResponse { IsSuccess = true });
            _sut = new NotifierConfigurationService(_repository.Object, validator.Object, BuiltInFixtures.Tenants().Object);
        }

        public void Dispose()
        {
            TestBlocksContext.Clear();
            GC.SuppressFinalize(this);
        }

        private void Stored(NotificationConfiguration configuration)
        {
            _repository.Setup(r => r.GetByNameAsync(configuration.Name)).ReturnsAsync(configuration);
            _repository.Setup(r => r.GetByIdAsync(configuration.ItemId)).ReturnsAsync(configuration);
        }

        private static NotifierSaveRequest Request(NotificationConfiguration stored, string? notifyMethod = null, bool? isDisabled = null) => new()
        {
            Name = stored.Name,
            ChannelToNotify = stored.ChannelToNotify,
            NotificationType = stored.NotificationType,
            NotifyMethod = notifyMethod ?? stored.NotifyMethod,
            EnablePersistence = stored.EnablePersistence,
            IsUpdateRequest = true,
            IsDisabled = isDisabled,
        };

        [Fact]
        public async Task ProjectChangesABuiltInField_IsRefused()
        {
            TestBlocksContext.Set(BuiltInFixtures.ProjectTenantId);
            var stored = BuiltInFixtures.BuiltIn(NotificationKind.Alert);
            Stored(stored);

            var response = await _sut.SaveConfigurationAsync(Request(stored, notifyMethod: "Other"));

            response.IsSuccess.Should().BeFalse();
            response.Errors.Should().ContainKey("NotifyMethod").WhoseValue.Should().Be(NotificationConfigurationWriteRules.BuiltInIsReadOnly);
            _repository.Verify(r => r.SaveAsync(It.IsAny<NotificationConfiguration>()), Times.Never);
        }

        [Fact]
        public async Task ProjectDisablesABuiltInAlert_IsSaved()
        {
            TestBlocksContext.Set(BuiltInFixtures.ProjectTenantId);
            var stored = BuiltInFixtures.BuiltIn(NotificationKind.Alert);
            Stored(stored);

            (await _sut.SaveConfigurationAsync(Request(stored, isDisabled: true))).IsSuccess.Should().BeTrue();
            _repository.Verify(r => r.SaveAsync(It.Is<NotificationConfiguration>(c => c.IsDisabled && c.IsBuiltIn)), Times.Once);
        }

        [Fact]
        public async Task ProjectDisablesABuiltInProgress_IsRefused()
        {
            TestBlocksContext.Set(BuiltInFixtures.ProjectTenantId);
            var stored = BuiltInFixtures.BuiltIn(NotificationKind.Progress);
            Stored(stored);

            var response = await _sut.SaveConfigurationAsync(Request(stored, isDisabled: true));

            response.Errors.Should().ContainKey("IsDisabled").WhoseValue.Should().Be(NotificationConfigurationWriteRules.CannotBeDisabled);
        }

        [Fact]
        public async Task ProjectDeletesABuiltIn_IsRefused_RootIsAllowed()
        {
            var stored = BuiltInFixtures.BuiltIn(NotificationKind.Alert);
            Stored(stored);

            TestBlocksContext.Set(BuiltInFixtures.ProjectTenantId);
            (await _sut.DeleteAsync(new NotifierDeleteRequest { ItemId = stored.ItemId })).IsSuccess.Should().BeFalse();
            _repository.Verify(r => r.DeleteConfigurationAsync(It.IsAny<NotifierDeleteRequest>()), Times.Never);

            TestBlocksContext.Set(BuiltInFixtures.RootTenantId);
            (await _sut.DeleteAsync(new NotifierDeleteRequest { ItemId = stored.ItemId })).IsSuccess.Should().BeTrue();
            _repository.Verify(r => r.DeleteConfigurationAsync(It.IsAny<NotifierDeleteRequest>()), Times.Once);
        }
    }
}
