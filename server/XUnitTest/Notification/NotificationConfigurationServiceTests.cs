using Blocks.Genesis;
using DomainService.Entities;
using DomainService.Shared;
using FluentAssertions;
using FluentValidation;
using FluentValidation.Results;
using Moq;
using XUnitTest.TestHelpers;
using ConfigurationService = DomainService.Configuration.Services.ConfigurationService;
using GetConfigurationsRequest = DomainService.Configuration.GetConfigurationsRequest;
using GetConfigurationsResponse = DomainService.Configuration.GetConfigurationsResponse;
using IConfigurationRepository = DomainService.Configuration.Services.IConfigurationRepository;
using SaveConfigurationRequest = DomainService.Configuration.SaveConfigurationRequest;

namespace XUnitTest.Notification
{
    /// <summary>
    /// Unit tests for the notification <see cref="ConfigurationService"/>. The repository and the
    /// validator are mocked, so the tests assert the mapping between the save request and the
    /// stored configuration as well as the pass through operations.
    /// </summary>
    public class NotificationConfigurationServiceTests : IDisposable
    {
        private readonly Mock<IConfigurationRepository> _repository = new();
        private readonly Mock<IValidator<SaveConfigurationRequest>> _validator = new();
        private readonly ConfigurationService _sut;

        public NotificationConfigurationServiceTests()
        {
            _validator.Setup(v => v.ValidateAsync(It.IsAny<SaveConfigurationRequest>(), It.IsAny<CancellationToken>()))
                      .ReturnsAsync(new ValidationResult());
            TestBlocksContext.Set("tenant-cfg", "user-cfg");

            _sut = new ConfigurationService(_repository.Object, _validator.Object);
        }

        public void Dispose()
        {
            TestBlocksContext.Clear();
            GC.SuppressFinalize(this);
        }

        private static SaveConfigurationRequest SaveRequest() => new()
        {
            Name = "welcome",
            ChannelToNotify = NotifierTypes.SignalR,
            NotificationType = NotificationReceiverTypes.BroadcastReceiverType,
            NotifyMethod = "ReceiveNotification",
            EnablePersistence = true,
        };

        [Fact]
        public async Task SaveConfigurationAsync_StoresANewConfigurationWhenTheNameIsUnknown()
        {
            _repository.Setup(r => r.GetByNameAsync("welcome")).ReturnsAsync((NotificationConfiguration)null!);
            NotificationConfiguration? stored = null;
            _repository.Setup(r => r.SaveAsync(It.IsAny<NotificationConfiguration>()))
                       .Callback<NotificationConfiguration>(c => stored = c)
                       .Returns(Task.CompletedTask);

            var result = await _sut.SaveConfigurationAsync(SaveRequest());

            result.IsSuccess.Should().BeTrue();
            stored.Should().NotBeNull();
            stored!.ItemId.Should().NotBeNullOrWhiteSpace();
            stored.Name.Should().Be("welcome");
            stored.NotifyMethod.Should().Be("ReceiveNotification");
            stored.ChannelToNotify.Should().Be(NotifierTypes.SignalR);
            stored.NotificationType.Should().Be(NotificationReceiverTypes.BroadcastReceiverType);
            stored.EnablePersistence.Should().BeTrue();
            stored.CreatedBy.Should().Be("user-cfg");
            stored.LastUpdatedBy.Should().Be("user-cfg");
        }

        [Fact]
        public async Task SaveConfigurationAsync_UpdatesTheStoredConfigurationInPlace()
        {
            var existing = new NotificationConfiguration
            {
                ItemId = "cfg-1",
                Name = "welcome",
                CreatedBy = "someone-else",
                CreatedDate = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                NotifyMethod = "OldMethod",
                EnablePersistence = false,
            };
            _repository.Setup(r => r.GetByNameAsync("welcome")).ReturnsAsync(existing);
            NotificationConfiguration? stored = null;
            _repository.Setup(r => r.SaveAsync(It.IsAny<NotificationConfiguration>()))
                       .Callback<NotificationConfiguration>(c => stored = c)
                       .Returns(Task.CompletedTask);

            var result = await _sut.SaveConfigurationAsync(SaveRequest());

            result.IsSuccess.Should().BeTrue();
            stored.Should().BeSameAs(existing);
            stored!.ItemId.Should().Be("cfg-1", "an update must keep the identity of the configuration");
            stored.CreatedBy.Should().Be("someone-else", "an update must not rewrite the author");
            stored.CreatedDate.Should().Be(new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc));
            stored.NotifyMethod.Should().Be("ReceiveNotification");
            stored.EnablePersistence.Should().BeTrue();
            stored.LastUpdatedBy.Should().Be("user-cfg");
            stored.LastUpdatedDate.Should().BeAfter(stored.CreatedDate);
        }

        [Fact]
        public async Task SaveConfigurationAsync_ReturnsTheValidationErrorsAndStoresNothing()
        {
            _validator.Setup(v => v.ValidateAsync(It.IsAny<SaveConfigurationRequest>(), It.IsAny<CancellationToken>()))
                      .ReturnsAsync(new ValidationResult([new ValidationFailure("Name", "Name is required.")]));

            var result = await _sut.SaveConfigurationAsync(SaveRequest());

            result.IsSuccess.Should().BeFalse();
            result.Errors.Should().ContainKey("Name").WhoseValue.Should().Be("Name is required.");
            _repository.Verify(r => r.SaveAsync(It.IsAny<NotificationConfiguration>()), Times.Never);
            _repository.Verify(r => r.GetByNameAsync(It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task GetsAsync_ReturnsThePageFromTheRepository()
        {
            var response = new GetConfigurationsResponse { TotalCount = 2, Configurations = [] };
            var request = new GetConfigurationsRequest();
            _repository.Setup(r => r.GetConfigurationsAsync(request)).ReturnsAsync(response);

            var result = await _sut.GetsAsync(request);

            result.Should().BeSameAs(response);
        }

    }
}
