using Api.Controllers;
using Blocks.Genesis;
using DomainService.Notification;
using DomainService.Shared;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using Blocks.Genesis.Entities.Logic;

namespace XUnitTest.Controllers
{
    public class NotifierControllerTests
    {
        private readonly Mock<INotificationService> _service = new();
        private readonly Mock<INotifierSendGuard> _guard = new();
        private readonly NotifierController _controller;

        public NotifierControllerTests()
        {
            _guard.Setup(g => g.IsAllowed(It.IsAny<string?>(), It.IsAny<string>())).Returns(true);
            _controller = new NotifierController(_service.Object, _guard.Object, Mock.Of<ILogger<NotifierController>>())
            {
                ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
            };
        }

        [Fact]
        public async Task Notify_ReturnsServiceResponse()
        {
            var response = new BaseResponse { IsSuccess = true };
            _service.Setup(s => s.NotifyAsync(It.IsAny<NotifyRequest>())).ReturnsAsync(response);

            var result = await _controller.Notify(new NotifyRequest());

            result.Value.Should().BeSameAs(response);
        }

        [Fact]
        public async Task SendSecretNotification_ReturnsServiceResponse()
        {
            var response = new BaseResponse { IsSuccess = true };
            _service.Setup(s => s.NotifyAsync(It.IsAny<NotifyRequest>())).ReturnsAsync(response);

            var result = await _controller.SendSecretNotification(new NotifyRequest());

            result.Value.Should().BeSameAs(response);
        }

        [Fact]
        public async Task Notify_PassesTheSecretHeaderToTheGuard()
        {
            _service.Setup(s => s.NotifyAsync(It.IsAny<NotifyRequest>())).ReturnsAsync(new BaseResponse());
            _controller.ControllerContext.HttpContext.Request.Headers["Secret"] = "header-value";

            await _controller.Notify(new NotifyRequest());

            _guard.Verify(g => g.IsAllowed("header-value", "Notify"), Times.Once);
        }

        [Fact]
        public async Task Notify_ReturnsUnauthorizedAndSendsNothingWhenTheGuardRefuses()
        {
            _guard.Setup(g => g.IsAllowed(It.IsAny<string?>(), It.IsAny<string>())).Returns(false);

            var result = await _controller.Notify(new NotifyRequest());

            result.Result.Should().BeOfType<UnauthorizedResult>();
            _service.Verify(s => s.NotifyAsync(It.IsAny<NotifyRequest>()), Times.Never);
        }

        [Fact]
        public async Task SendSecretNotification_ReturnsUnauthorizedAndSendsNothingWhenTheGuardRefuses()
        {
            _guard.Setup(g => g.IsAllowed(It.IsAny<string?>(), It.IsAny<string>())).Returns(false);

            var result = await _controller.SendSecretNotification(new NotifyRequest());

            result.Result.Should().BeOfType<UnauthorizedResult>();
            _service.Verify(s => s.NotifyAsync(It.IsAny<NotifyRequest>()), Times.Never);
            _guard.Verify(g => g.IsAllowed(It.IsAny<string?>(), "SendSecretNotification"), Times.Once);
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
