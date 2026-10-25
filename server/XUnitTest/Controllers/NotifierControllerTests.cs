using Api.Controllers;
using Blocks.Genesis;
using BlocksTemplate.Api.Security;
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
        private readonly Mock<INotifierSendPermission> _permission = new();
        private readonly NotifierController _controller;

        public NotifierControllerTests()
        {
            GuardReturns(true);
            _controller = new NotifierController(_service.Object, _guard.Object, _permission.Object,
                Mock.Of<ILogger<NotifierController>>())
            {
                ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
            };
        }

        private void GuardReturns(bool allowed) =>
            _guard.Setup(g => g.IsAllowedAsync(It.IsAny<string?>(), It.IsAny<Func<Task<NotifierCallerToken>>>(),
                It.IsAny<string>())).ReturnsAsync(allowed);

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
            await _controller.Notify(new NotifyRequest(), "header-value");

            _guard.Verify(g => g.IsAllowedAsync("header-value", It.IsAny<Func<Task<NotifierCallerToken>>>(), "Notify"),
                Times.Once);
        }

        [Fact]
        public async Task Notify_ReturnsUnauthorizedAndSendsNothingWhenTheGuardRefuses()
        {
            GuardReturns(false);

            var result = await _controller.Notify(new NotifyRequest());

            result.Result.Should().BeOfType<UnauthorizedResult>();
            _service.Verify(s => s.NotifyAsync(It.IsAny<NotifyRequest>()), Times.Never);
        }

        [Fact]
        public async Task SendSecretNotification_ReturnsUnauthorizedAndSendsNothingWhenTheGuardRefuses()
        {
            GuardReturns(false);

            var result = await _controller.SendSecretNotification(new NotifyRequest());

            result.Result.Should().BeOfType<UnauthorizedResult>();
            _service.Verify(s => s.NotifyAsync(It.IsAny<NotifyRequest>()), Times.Never);
            _guard.Verify(g => g.IsAllowedAsync(It.IsAny<string?>(), It.IsAny<Func<Task<NotifierCallerToken>>>(),
                "SendSecretNotification"), Times.Once);
        }

        [Fact]
        public async Task Notify_TheGuardsTokenCheckAsksThePermissionCheckForThisRequest()
        {
            Func<Task<NotifierCallerToken>> captured = () => Task.FromResult(NotifierCallerToken.None);
            _guard.Setup(g => g.IsAllowedAsync(It.IsAny<string?>(), It.IsAny<Func<Task<NotifierCallerToken>>>(),
                    It.IsAny<string>()))
                .Callback((string? _, Func<Task<NotifierCallerToken>> check, string _) => captured = check)
                .ReturnsAsync(false);
            _permission.Setup(p => p.CheckAsync(_controller.ControllerContext.HttpContext))
                .ReturnsAsync(NotifierCallerToken.Permitted);

            await _controller.Notify(new NotifyRequest());

            (await captured()).Should().Be(NotifierCallerToken.Permitted);
        }

        [Fact]
        public async Task Notify_WritesNothingMoreWhenTheResponseHasAlreadyStarted()
        {
            GuardReturns(false);
            var started = new Mock<Microsoft.AspNetCore.Http.Features.IHttpResponseFeature>();
            started.SetupGet(f => f.HasStarted).Returns(true);
            started.SetupProperty(f => f.StatusCode, StatusCodes.Status429TooManyRequests);
            started.SetupProperty(f => f.Headers, new HeaderDictionary());
            _controller.ControllerContext.HttpContext.Features.Set(started.Object);

            var result = await _controller.Notify(new NotifyRequest());

            result.Result.Should().BeOfType<EmptyResult>();
            _service.Verify(s => s.NotifyAsync(It.IsAny<NotifyRequest>()), Times.Never);
        }

        [Fact]
        public void SendActions_ValidateATokenButStayOpenWithoutOne()
        {
            foreach (var action in new[] { nameof(NotifierController.Notify), nameof(NotifierController.SendSecretNotification) })
            {
                var method = typeof(NotifierController).GetMethod(action)!;
                method.GetCustomAttributes(typeof(NotifierOptionalTokenAttribute), false).Should().ContainSingle();
                method.GetCustomAttributes(typeof(Microsoft.AspNetCore.Authorization.AuthorizeAttribute), true)
                    .Should().BeEmpty();
            }
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
