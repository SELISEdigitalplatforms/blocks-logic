using System.Linq.Expressions;
using Blocks.Genesis;
using DomainService.Notification;
using DomainService.Shared;
using FluentAssertions;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace XUnitTest.Notification
{
    public class WebPushSubscriptionServiceTests : IDisposable
    {
        private readonly Mock<INotificationRepository> _repo = new();
        private readonly Mock<IWebPushVapidKeyService> _vapid = new();
        private readonly List<WebPushSubscription> _store = [];
        private readonly NotificationService _sut;

        public WebPushSubscriptionServiceTests()
        {
            BlocksContext.IsTestMode = true;
            _repo.Setup(r => r.DeleteAsync(It.IsAny<Expression<Func<WebPushSubscription, bool>>>()))
                .Returns<Expression<Func<WebPushSubscription, bool>>>(expr =>
                {
                    var compiled = expr.Compile();
                    _store.RemoveAll(s => compiled(s));
                    return Task.CompletedTask;
                });
            _repo.Setup(r => r.SaveAsync(It.IsAny<WebPushSubscription>(), It.IsAny<string>()))
                .Returns<WebPushSubscription, string>((s, _) =>
                {
                    _store.Add(s);
                    return Task.CompletedTask;
                });
            _repo.Setup(r => r.GetItemsAsync(It.IsAny<Expression<Func<WebPushSubscription, bool>>>(), It.IsAny<string>()))
                .ReturnsAsync((Expression<Func<WebPushSubscription, bool>> expr, string _) =>
                    _store.Where(expr.Compile()).ToList());

            _vapid.Setup(v => v.GetOrCreatePublicKeyAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync("BKx7public");
            _vapid.Setup(v => v.RotateAsync(It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

            _sut = new NotificationService(
                _repo.Object,
                new NotificationServiceValidators(
                    Mock.Of<IValidator<Subscription>>(),
                    Mock.Of<IValidator<NotifyRequest>>(),
                    new RegisterWebPushSubscriptionRequestValidator(),
                    new UnregisterWebPushSubscriptionRequestValidator()),
                NullLogger<NotificationService>.Instance,
                Mock.Of<INotifierServiceFactory>(),
                Mock.Of<DomainService.Configuration.Services.IConfigurationRepository>(),
                _vapid.Object);
        }

        public void Dispose()
        {
            BlocksContext.SetContext(null);
            BlocksContext.IsTestMode = false;
        }

        private static void SetUser(string userId) =>
            BlocksContext.SetContext(BlocksContext.Create(
                "t1", null, userId, true, null, null,
                DateTime.UtcNow.AddHours(1), null, null, null, null, null, null, "", "t1"));

        private static RegisterWebPushSubscriptionRequest ValidRequest(string endpoint = "https://fcm.googleapis.com/fcm/send/xyz") =>
            new()
            {
                Endpoint = endpoint,
                Keys = new RegisterWebPushSubscriptionKeys { P256dh = "BN4abc", Auth = "k8Jxyz" },
            };

        [Fact]
        public async Task Register_PersistsSubscriptionForCaller_H1()
        {
            SetUser("U1");

            var result = await _sut.RegisterWebPushSubscriptionAsync(ValidRequest());

            result.IsSuccess.Should().BeTrue();
            _store.Should().ContainSingle(s => s.UserId == "U1" && s.Endpoint.Contains("xyz") && s.Keys.P256dh == "BN4abc");
        }

        [Fact]
        public async Task Register_UpsertsSameEndpoint_H1()
        {
            SetUser("U1");
            await _sut.RegisterWebPushSubscriptionAsync(ValidRequest());
            var second = ValidRequest();
            second.Keys.P256dh = "NEWkey";
            await _sut.RegisterWebPushSubscriptionAsync(second);

            _store.Should().ContainSingle();
            _store[0].Keys.P256dh.Should().Be("NEWkey");
        }

        [Fact]
        public async Task Register_RejectsMissingAuth_C1()
        {
            SetUser("U1");
            var request = ValidRequest();
            request.Keys.Auth = "";

            var result = await _sut.RegisterWebPushSubscriptionAsync(request);

            result.IsSuccess.Should().BeFalse();
            result.Errors.Should().ContainKey("Keys.Auth");
            _store.Should().BeEmpty();
        }

        [Fact]
        public async Task Unregister_RemovesSubscription_H5()
        {
            SetUser("U1");
            await _sut.RegisterWebPushSubscriptionAsync(ValidRequest());

            var result = await _sut.UnregisterWebPushSubscriptionAsync(new UnregisterWebPushSubscriptionRequest
            {
                Endpoint = "https://fcm.googleapis.com/fcm/send/xyz"
            });

            result.IsSuccess.Should().BeTrue();
            _store.Should().BeEmpty();
        }

        [Fact]
        public async Task Unregister_MissingEndpointIsNoOpSuccess_H5()
        {
            SetUser("U1");

            var result = await _sut.UnregisterWebPushSubscriptionAsync(new UnregisterWebPushSubscriptionRequest
            {
                Endpoint = "https://example.com/missing"
            });

            result.IsSuccess.Should().BeTrue();
        }

        [Fact]
        public async Task GetPublicKey_ReturnsVapidPublicKey_H2()
        {
            SetUser("U1");
            var result = await _sut.GetWebPushPublicKeyAsync();
            result.PublicKey.Should().Be("BKx7public");
        }

        [Fact]
        public async Task Rotate_ClearsAllSubscriptions_H6_C6()
        {
            SetUser("U1");
            await _sut.RegisterWebPushSubscriptionAsync(ValidRequest());
            await _sut.RegisterWebPushSubscriptionAsync(ValidRequest("https://fcm.googleapis.com/fcm/send/other"));

            var result = await _sut.RotateWebPushVapidKeysAsync();

            result.IsSuccess.Should().BeTrue();
            _vapid.Verify(v => v.RotateAsync(It.IsAny<CancellationToken>()), Times.Once);
            _store.Should().BeEmpty();
        }

        [Fact]
        public async Task Notify_UnknownConfiguration_ReturnsError_C2()
        {
            var configRepo = new Mock<DomainService.Configuration.Services.IConfigurationRepository>();
            configRepo.Setup(c => c.GetByNameAsync("does-not-exist")).ReturnsAsync((DomainService.Entities.NotificationConfiguration)null!);
            var notifyValidator = new Mock<IValidator<NotifyRequest>>();
            notifyValidator.Setup(v => v.ValidateAsync(It.IsAny<NotifyRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ValidationResult());

            var sut = new NotificationService(
                _repo.Object,
                new NotificationServiceValidators(
                    Mock.Of<IValidator<Subscription>>(),
                    notifyValidator.Object,
                    new RegisterWebPushSubscriptionRequestValidator(),
                    new UnregisterWebPushSubscriptionRequestValidator()),
                NullLogger<NotificationService>.Instance,
                Mock.Of<INotifierServiceFactory>(),
                configRepo.Object,
                _vapid.Object);

            var result = await sut.NotifyAsync(new NotifyRequest { ConfigurationName = "does-not-exist", UserIds = ["U1"] });

            result.IsSuccess.Should().BeFalse();
            result.Errors.Should().ContainKey("configurationName");
        }
    }
}
