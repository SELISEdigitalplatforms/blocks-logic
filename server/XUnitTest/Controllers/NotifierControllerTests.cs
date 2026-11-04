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
        private readonly Mock<INotifyService> _service = new();
        private readonly Mock<INotificationService> _inbox = new();
        private readonly Mock<INotifierSendGuard> _guard = new();
        private readonly Mock<INotifierSendPermission> _permission = new();
        private readonly NotifierController _controller;

        public NotifierControllerTests()
        {
            GuardReturns(true);
            _controller = new NotifierController(_inbox.Object, _service.Object, _guard.Object, _permission.Object,
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
            _inbox.Setup(s => s.GetUnreadNotificationsBySubscriptionFilter(
                It.IsAny<GetUnreadNotificationsRequestBySubscriptionFilter>())).ReturnsAsync(list);

            var result = await _controller.GetUnreadNotificationsBySubscriptionFilter(
                new GetUnreadNotificationsRequestBySubscriptionFilter());

            result.Should().BeSameAs(list);
        }

        [Fact]
        public async Task MarkAllNotificationAsRead_ReturnsResponse()
        {
            var response = new BaseResponse { IsSuccess = true };
            _inbox.Setup(s => s.MarkAllNotificationAsRead()).ReturnsAsync(response);

            var result = await _controller.MarkAllNotificationAsRead();

            result.Should().BeSameAs(response);
        }

        [Fact]
        public async Task MarkNotificationAsRead_ReturnsResponse()
        {
            var response = new BaseResponse { IsSuccess = true };
            _inbox.Setup(s => s.MarkNotificationAsRead(It.IsAny<MarkNotificationAsReadRequest>()))
                .ReturnsAsync(response);

            var result = await _controller.MarkNotificationAsRead(new MarkNotificationAsReadRequest { Id = "n1" });

            result.Should().BeSameAs(response);
        }

        [Fact]
        public async Task GetNotifications_ReturnsResponse()
        {
            var response = new GetNotificationsResponse();
            _inbox.Setup(s => s.GetNotificationsAsync(It.IsAny<GetNotificationsRequest>()))
                .ReturnsAsync(response);

            var result = await _controller.GetNotifications(new GetNotificationsRequest());

            result.Value.Should().BeSameAs(response);
        }

        [Theory]
        [InlineData(0, 10)]   // H5
        [InlineData(2, 10)]   // H6
        [InlineData(0, 100)]  // H7
        public async Task GetNotifications_ValidPaging_PassesTheRequestThroughUnchanged(int page, int pageSize)
        {
            var response = new GetNotificationsResponse();
            var request = new GetNotificationsRequest { Page = page, PageSize = pageSize };
            _inbox.Setup(s => s.GetNotificationsAsync(request)).ReturnsAsync(response);

            var result = await _controller.GetNotifications(request);

            result.Result.Should().BeNull("a valid page is the plain 200 body, as before");
            result.Value.Should().BeSameAs(response);
            request.Page.Should().Be(page);
            request.PageSize.Should().Be(pageSize);
        }

        [Theory]
        [InlineData(-1, 10, new[] { "Page" }, new[] { "Page must be 0 or greater." })]                                     // C1
        [InlineData(0, 0, new[] { "PageSize" }, new[] { "PageSize must be between 1 and 100." })]                            // C2
        [InlineData(0, 101, new[] { "PageSize" }, new[] { "PageSize must be between 1 and 100." })]                          // C2
        [InlineData(int.MaxValue, 10, new[] { "Page" }, new[] { "Page is too large for this PageSize." })]                   // C3
        [InlineData(-1, 0, new[] { "Page", "PageSize" }, new[] { "Page must be 0 or greater.", "PageSize must be between 1 and 100." })] // C4
        public async Task GetNotifications_InvalidPaging_Is400WithTheErrors_BeforeAnyLookup(
            int page, int pageSize, string[] keys, string[] messages)
        {
            var logger = new Mock<ILogger<NotifierController>>();
            var controller = new NotifierController(_inbox.Object, _service.Object, _guard.Object, _permission.Object, logger.Object)
            {
                ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
            };

            var result = await controller.GetNotifications(new GetNotificationsRequest { Page = page, PageSize = pageSize });

            var badRequest = result.Result.Should().BeOfType<BadRequestObjectResult>().Subject;
            badRequest.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
            var body = badRequest.Value.Should().BeOfType<BaseResponse>().Subject;
            body.IsSuccess.Should().BeFalse();
            body.Errors.Should().BeEquivalentTo(keys.Zip(messages).ToDictionary(p => p.First, p => p.Second));
            // C1-C4: no user (ViewerAsync) or database lookup; C5: no Error log, the 400 is not an exception.
            _inbox.VerifyNoOtherCalls();
            logger.Verify(l => l.Log(LogLevel.Error, It.IsAny<EventId>(), It.IsAny<It.IsAnyType>(), It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.Never);
        }
    }
}
