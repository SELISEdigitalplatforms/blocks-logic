using Blocks.Genesis.Entities.Os;
using Blocks.Genesis.Notifications;
using DomainService.Notification.Push;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using static XUnitTest.Notification.Push.PushTestKit;

namespace XUnitTest.Notification.Push
{
    /// <summary>
    /// The push worker's order of checks, where each receiver's devices are read, which platforms get the push, and
    /// that a failure before FCM releases the dedup claim.
    /// </summary>
    public class PushRequestHandlerTests
    {
        private const string Tenant = "tenant-1";
        private static readonly DateTime Now = new(2026, 10, 28, 12, 0, 0, DateTimeKind.Utc);

        private readonly Mock<IPushDedupStore> _dedup = new();
        private readonly Mock<IPushSettingsStore> _settings = new();
        private readonly Mock<IPushMessageComposer> _composer = new();
        private readonly Mock<IPushDeviceStore> _devices = new();
        private readonly Mock<IFcmPushSender> _sender = new();
        private readonly List<PushNote> _sent = [];
        private readonly PushRequestHandler _sut;

        private sealed class FixedTime : TimeProvider
        {
            public override DateTimeOffset GetUtcNow() => Now;
        }

        public PushRequestHandlerTests()
        {
            _dedup.Setup(d => d.TryClaimAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
            _settings.Setup(s => s.GetAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Settings(web: true));
            _composer.Setup(c => c.PrepareAsync(It.IsAny<PushRequested>(), It.IsAny<CancellationToken>())).ReturnsAsync(Composition());
            _devices.Setup(d => d.GetForUsersAsync(It.Is<PushDb>(db => !db.Root), It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync((PushDb _, IReadOnlyCollection<string> ids, DateTime _, CancellationToken _) =>
                        ids.Select(id => Device(id, "project-token-" + id, PlatformOf(id))).ToList());
            _devices.Setup(d => d.GetForUsersAsync(It.Is<PushDb>(db => db.Root), It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync((PushDb _, IReadOnlyCollection<string> ids, DateTime _, CancellationToken _) =>
                        ids.Select(id => Device(id, "root-token-" + id, PlatformOf(id))).ToList());
            _sender.Setup(s => s.OpenAsync(Tenant, It.IsAny<PushSettingsRecord>(), It.IsAny<CancellationToken>()))
                   .ReturnsAsync(new FcmOpening(Session(Tenant), FcmOpenFailure.None));
            _sender.Setup(s => s.SendAsync(It.IsAny<FcmSession>(), It.IsAny<IReadOnlyList<PushNote>>(), It.IsAny<CancellationToken>()))
                   .Callback<FcmSession, IReadOnlyList<PushNote>, CancellationToken>((_, notes, _) => _sent.AddRange(notes))
                   .ReturnsAsync(new PushSendSummary(1, 0, 0, []));
            _sut = new PushRequestHandler(_dedup.Object, _settings.Object, _composer.Object, _devices.Object, _sender.Object,
                new FixedTime(), NullLogger<PushRequestHandler>.Instance);
        }

        /// <summary>User ids starting with a platform name have a device of that platform; others android.</summary>
        private static string PlatformOf(string userId) =>
            PushPlatforms.All.FirstOrDefault(p => userId.StartsWith(p + "-", StringComparison.Ordinal)) ?? PushPlatforms.Android;

        private static PushComposition Composition(PushNotificationOptions? options = null) => new(
            new NotificationPushTemplate { TitleKey = "t", BodyKey = "b", Link = "app://x/{{id}}" },
            new PushKeyTexts("en-US", new Dictionary<string, IReadOnlyDictionary<string, string>>
            {
                ["t"] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["en-US"] = "Hello {{name}}", ["de-DE"] = "Hallo {{name}}" },
                ["b"] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["en-US"] = "Body" },
            }),
            new Dictionary<string, string> { ["german"] = "de-DE" },
            new Dictionary<string, string> { ["name"] = "Ada", ["id"] = "42" },
            options);

        private static PushRequested Message(TimeSpan? age = null, string[]? users = null, string[]? rootUsers = null) => new()
        {
            Id = Guid.NewGuid(),
            TenantId = Tenant,
            ConfigName = "order-created",
            UserIds = users ?? ["project-user", "root-user"],
            RootUserIds = rootUsers ?? ["root-user"],
            Payload = "{}",
            SentAt = Now - (age ?? TimeSpan.FromMinutes(1)),
        };

        [Fact]
        public async Task ProjectUsersUseTheTenantDb_RootUsersTheRootDb()
        {
            await _sut.HandleAsync(Message(), CancellationToken.None);

            _sent.Select(n => (n.Device.Token, n.Db.Root)).Should().BeEquivalentTo(new[]
            {
                ("project-token-project-user", false),
                ("root-token-root-user", true),
            });
            _sent.Should().OnlyContain(n => n.Data["configName"] == "order-created" && n.Data["link"] == "app://x/42");
        }

        [Fact]
        public async Task EachReceiverGetsTheTextInTheirLanguage()
        {
            await _sut.HandleAsync(Message(users: ["german", "someone"], rootUsers: []), CancellationToken.None);

            _sent.Single(n => n.Device.UserId == "german").Title.Should().Be("Hallo Ada");
            _sent.Single(n => n.Device.UserId == "someone").Title.Should().Be("Hello Ada");
        }

        [Fact]
        public async Task ADuplicate_IsNotSentAgain()
        {
            _dedup.Setup(d => d.TryClaimAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);

            await _sut.HandleAsync(Message(), CancellationToken.None);

            _sender.Verify(s => s.SendAsync(It.IsAny<FcmSession>(), It.IsAny<IReadOnlyList<PushNote>>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task AMessageOlderThanOneHour_IsDropped_BeforeTheDedupClaim()
        {
            await _sut.HandleAsync(Message(age: TimeSpan.FromMinutes(61)), CancellationToken.None);

            _dedup.Verify(d => d.TryClaimAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
            _sent.Should().BeEmpty();
        }

        public static TheoryData<PushSettingsRecord?> NotSendable() => new()
        {
            null,
            Settings(enabled: false),
            new PushSettingsRecord { ProjectId = ProjectId, Enabled = true },
        };

        [Theory]
        [MemberData(nameof(NotSendable))]
        public async Task PushOffMissingOrWithoutAKey_IsDropped(PushSettingsRecord? settings)
        {
            _settings.Setup(s => s.GetAsync(It.IsAny<CancellationToken>())).ReturnsAsync(settings);

            await _sut.HandleAsync(Message(), CancellationToken.None);

            _sent.Should().BeEmpty();
            _composer.Verify(c => c.PrepareAsync(It.IsAny<PushRequested>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task AConfigurationWithoutAPushTemplate_IsDropped()
        {
            _composer.Setup(c => c.PrepareAsync(It.IsAny<PushRequested>(), It.IsAny<CancellationToken>())).ReturnsAsync((PushComposition?)null);

            await _sut.HandleAsync(Message(), CancellationToken.None);

            _sent.Should().BeEmpty();
        }

        [Fact]
        public async Task AnIncompleteMessage_IsDropped()
        {
            await _sut.HandleAsync(Message() with { TenantId = "" }, CancellationToken.None);
            await _sut.HandleAsync(Message() with { UserIds = [] }, CancellationToken.None);
            await _sut.HandleAsync(Message() with { Id = Guid.Empty }, CancellationToken.None);

            _dedup.Verify(d => d.TryClaimAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task DevicesNotSeenFor60Days_AreNotRead()
        {
            await _sut.HandleAsync(Message(), CancellationToken.None);

            _devices.Verify(d => d.GetForUsersAsync(It.IsAny<PushDb>(), It.IsAny<IReadOnlyCollection<string>>(), Now.AddDays(-60), It.IsAny<CancellationToken>()),
                Times.Exactly(2));
        }

        [Fact]
        public async Task APlatformSwitchedOff_GetsNothing()
        {
            _settings.Setup(s => s.GetAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Settings(ios: false, web: true));

            await _sut.HandleAsync(Message(users: ["android-1", "ios-1", "web-1"], rootUsers: []), CancellationToken.None);

            _sent.Select(n => n.Device.Platform).Should().BeEquivalentTo("android", "web");
        }

        [Fact]
        public async Task PushSwitchedOffWhileTheMessageWasPrepared_SendsNothing()
        {
            _settings.SetupSequence(s => s.GetAsync(It.IsAny<CancellationToken>()))
                     .ReturnsAsync(Settings())
                     .ReturnsAsync(Settings(enabled: false));

            await _sut.HandleAsync(Message(), CancellationToken.None);

            _sent.Should().BeEmpty();
            _sender.Verify(s => s.OpenAsync(It.IsAny<string>(), It.IsAny<PushSettingsRecord>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task OnlyThePlatformsChosenForTheNotification_GetIt_AndWebGetsTheWebLinkNotTheAppLink()
        {
            _composer.Setup(c => c.PrepareAsync(It.IsAny<PushRequested>(), It.IsAny<CancellationToken>()))
                     .ReturnsAsync(Composition(new PushNotificationOptions { Platforms = ["ios", "web"], WebLink = "https://x.example.com/{{id}}" }));

            await _sut.HandleAsync(Message(users: ["android-1", "ios-1", "web-1"], rootUsers: []), CancellationToken.None);

            _sent.Select(n => n.Device.Platform).Should().BeEquivalentTo("ios", "web");
            _sent.Single(n => n.Device.Platform == "ios").Data["link"].Should().Be("app://x/42");
            var web = _sent.Single(n => n.Device.Platform == "web");
            web.Data.Should().NotContainKey("link");
            web.Blocks!.WebLink.Should().Be("https://x.example.com/42");
        }

        [Fact]
        public async Task Bug_AFailureBeforeFcmKeptTheClaim_NowTheClaimIsReleased_SoARedeliveryCanSend()
        {
            _sender.Setup(s => s.OpenAsync(Tenant, It.IsAny<PushSettingsRecord>(), It.IsAny<CancellationToken>()))
                   .ThrowsAsync(new PushKeyUnavailableException());
            var message = Message();

            var handle = () => _sut.HandleAsync(message, CancellationToken.None);

            await handle.Should().ThrowAsync<PushKeyUnavailableException>();
            _dedup.Verify(d => d.ReleaseAsync(message.Id, It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task AFailureDuringTheFcmStep_KeepsTheClaim_SoNothingIsSentTwice()
        {
            _sender.Setup(s => s.SendAsync(It.IsAny<FcmSession>(), It.IsAny<IReadOnlyList<PushNote>>(), It.IsAny<CancellationToken>()))
                   .ThrowsAsync(new HttpRequestException("network"));

            var handle = () => _sut.HandleAsync(Message(), CancellationToken.None);

            await handle.Should().ThrowAsync<HttpRequestException>();
            _dedup.Verify(d => d.ReleaseAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task AMissingKey_DropsThePush_WithoutThrowing()
        {
            _sender.Setup(s => s.OpenAsync(Tenant, It.IsAny<PushSettingsRecord>(), It.IsAny<CancellationToken>()))
                   .ReturnsAsync(new FcmOpening(null, FcmOpenFailure.NoKey));

            await _sut.HandleAsync(Message(), CancellationToken.None);

            _sent.Should().BeEmpty();
        }
    }
}
