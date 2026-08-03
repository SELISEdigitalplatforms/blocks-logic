using Blocks.Genesis;
using BlocksTemplate.Api.Controllers;
using CloudConfiguration.DomainService.Notification.Entities;
using CloudConfiguration.DomainService.Notification.RequestModel;
using CloudConfiguration.DomainService.Notification.ResponseModel;
using CloudConfiguration.DomainService.Shared.Services;
using FluentAssertions;
using Moq;

namespace XUnitTest.Controllers
{
    public class NotificationConfigControllerTests
    {
        private readonly Mock<IConfigurationService> _config = new();
        private readonly NotificationController _controller;

        public NotificationConfigControllerTests()
        {
            _controller = new NotificationController(_config.Object);
        }

        [Fact]
        public async Task Save_ReturnsServiceResponse()
        {
            var response = new BaseResponse { IsSuccess = true };
            _config.Setup(s => s.SaveNotificationConfigurationAsync(It.IsAny<SaveNotificatonConfigurationRequest>()))
                .ReturnsAsync(response);

            var result = await _controller.Save(new SaveNotificatonConfigurationRequest());

            result.Should().BeSameAs(response);
        }

        [Fact]
        public async Task Gets_ReturnsResponse()
        {
            var response = new GetNotificationConfigurationsResponse();
            _config.Setup(s => s.GetNotificationConfigurationsAsync(It.IsAny<GetNotificationConfigurationsRequest>()))
                .ReturnsAsync(response);

            var result = await _controller.Gets(new GetNotificationConfigurationsRequest());

            result.Should().BeSameAs(response);
        }

        [Fact]
        public async Task Get_ReturnsConfiguration()
        {
            var config = new NotificationConfiguration();
            _config.Setup(s => s.GetNotificatoinConfigurationAsync(It.IsAny<GetNotificationConfigurationRequest>()))
                .ReturnsAsync(config);

            var result = await _controller.Get(new GetNotificationConfigurationRequest());

            result.Should().BeSameAs(config);
        }

        [Fact]
        public async Task Delete_ReturnsServiceResponse()
        {
            var response = new BaseResponse { IsSuccess = true };
            _config.Setup(s => s.DeleteNotificationConfigurationAsync(It.IsAny<DeleteNotificatoinConfigurationRequest>()))
                .ReturnsAsync(response);

            var result = await _controller.Delete(new DeleteNotificatoinConfigurationRequest());

            result.Should().BeSameAs(response);
        }
    }
}
