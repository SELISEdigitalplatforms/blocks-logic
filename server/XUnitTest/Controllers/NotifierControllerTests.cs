using Api.Controllers;
using Blocks.Genesis;
using DomainService.Notification;
using DomainService.Shared;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;

namespace XUnitTest.Controllers
{
    public class NotifierControllerTests
    {
        private readonly Mock<INotificationService> _service = new();
        private readonly NotifierController _controller;

        public NotifierControllerTests()
        {
            _controller = new NotifierController(_service.Object, Mock.Of<ILogger<NotifierController>>());
        }

        [Fact]
        public async Task Notify_ReturnsServiceResponse()
        {
            var response = new BaseResponse { IsSuccess = true };
            _service.Setup(s => s.NotifyAsync(It.IsAny<NotifyRequest>())).ReturnsAsync(response);

            var result = await _controller.Notify(new NotifyRequest());

            result.Should().BeSameAs(response);
        }

        [Fact]
        public async Task SendSecretNotification_ReturnsServiceResponse()
        {
            var response = new BaseResponse { IsSuccess = true };
            _service.Setup(s => s.NotifyAsync(It.IsAny<NotifyRequest>())).ReturnsAsync(response);

            var result = await _controller.SendSecretNotification(new NotifyRequest());

            result.Should().BeSameAs(response);
        }

        [Fact]
        public async Task GetUnreadNotifications_ReturnsList()
        {
            var list = new List<OfflineNotification> { new() };
            _service.Setup(s => s.GetUnreadNotificationsBySubscriptionFilter(
                It.IsAny<GetUnreadNotificationsRequestBySubscriptionFilter>())).ReturnsAsync(list);

            var result = await _controller.GetUnreadNotificationsBySubscriptionFilter(
                new GetUnreadNotificationsRequestBySubscriptionFilter());

            result.Should().BeSameAs(list);
        }

        [Fact]
        public async Task MarkAllNotificationAsRead_ReturnsResponse()
        {
            var response = new BaseResponse { IsSuccess = true };
            _service.Setup(s => s.MarkAllNotificationAsRead()).ReturnsAsync(response);

            var result = await _controller.MarkAllNotificationAsRead();

            result.Should().BeSameAs(response);
        }

        [Fact]
        public async Task MarkNotificationAsRead_ReturnsResponse()
        {
            var response = new BaseResponse { IsSuccess = true };
            _service.Setup(s => s.MarkNotificationAsRead(It.IsAny<MarkNotificationAsReadRequest>()))
                .ReturnsAsync(response);

            var result = await _controller.MarkNotificationAsRead(new MarkNotificationAsReadRequest { Id = "n1" });

            result.Should().BeSameAs(response);
        }

        [Fact]
        public async Task GetNotifications_ReturnsResponse()
        {
            var response = new GetNotificationsResponse();
            _service.Setup(s => s.GetNotificationsAsync(It.IsAny<GetNotificationsRequest>()))
                .ReturnsAsync(response);

            var result = await _controller.GetNotifications(new GetNotificationsRequest());

            result.Should().BeSameAs(response);
        }
    }
}
