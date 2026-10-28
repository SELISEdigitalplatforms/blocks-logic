using Blocks.Genesis.Entities.Os;
using Blocks.Genesis.Notifications;
using DomainService.Notification.Push;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using static XUnitTest.Notification.Push.PushTestKit;

namespace XUnitTest.Notification.Push
{
    /// <summary>The push worker's order of checks and where each receiver's devices are read.</summary>
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
            _settings.Setup(s => s.GetAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Settings());
            _composer.Setup(c => c.PrepareAsync(It.IsAny<PushRequested>(), It.IsAny<CancellationToken>())).ReturnsAsync(Composition());
            _devices.Setup(d => d.GetForUsersAsync(It.Is<PushDb>(db => !db.Root), It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync((PushDb _, IReadOnlyCollection<string> ids, CancellationToken _) =>
                        ids.Select(id => Device(id, "project-token-" + id)).ToList());
            _devices.Setup(d => d.GetForUsersAsync(It.Is<PushDb>(db => db.Root), It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync((PushDb _, IReadOnlyCollection<string> ids, CancellationToken _) =>
                        ids.Select(id => Device(id, "root-token-" + id)).ToList());
            _sender.Setup(s => s.SendAsync(Tenant, It.IsAny<PushSettingsRecord>(), It.IsAny<IReadOnlyList<PushNote>>(), It.IsAny<CancellationToken>()))
                   .Callback<string, PushSettingsRecord, IReadOnlyList<PushNote>, CancellationToken>((_, _, notes, _) => _sent.AddRange(notes))
                   .ReturnsAsync(new PushSendSummary(1, 0, 0));
            _sut = new PushRequestHandler(_dedup.Object, _settings.Object, _composer.Object, _devices.Object, _sender.Object,
                new FixedTime(), NullLogger<PushRequestHandler>.Instance);
        }

        private static PushComposition Composition() => new(
            new NotificationPushTemplate { TitleKey = "t", BodyKey = "b", Link = "app://x/{{id}}" },
            new PushKeyTexts("en-US", new Dictionary<string, IReadOnlyDictionary<string, string>>
            {
                ["t"] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["en-US"] = "Hello {{name}}", ["de-DE"] = "Hallo {{name}}" },
                ["b"] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["en-US"] = "Body" },
            }),
            new Dictionary<string, string> { ["german"] = "de-DE" },
            new Dictionary<string, string> { ["name"] = "Ada", ["id"] = "42" });

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

            _sender.Verify(s => s.SendAsync(It.IsAny<string>(), It.IsAny<PushSettingsRecord>(), It.IsAny<IReadOnlyList<PushNote>>(), It.IsAny<CancellationToken>()), Times.Never);
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
    }
}
