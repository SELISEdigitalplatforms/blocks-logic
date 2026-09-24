using Blocks.Secrets;
using FluentAssertions;
using Mail.DomainService.Entities;
using Mail.DomainService.Mails.Office365;
using Mail.DomainService.Shared.Enums;
using MailBoxSyncService.Entities;
using MailBoxSyncService.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Graph.Models.ODataErrors;
using MimeKit;
using Moq;

namespace XUnitTest.MailBoxSyncService
{
    public class Office365GraphInboxPollerTests
    {
        private const string Mailbox = "support@contoso.com";
        private const string NextLink = "https://graph.microsoft.com/v1.0/users/support%40contoso.com/mailFolders/inbox/messages/delta?%24skiptoken=page-2";
        private const string DeltaLink = "https://graph.microsoft.com/v1.0/users/support%40contoso.com/mailFolders/inbox/messages/delta?%24deltatoken=round-1";

        private readonly Mock<IMailBoxSyncService> _sync = new();
        private readonly Mock<IMailRepository> _repository = new();
        private readonly Mock<IOffice365TokenProvider> _tokens = new();
        private readonly Mock<IOffice365GraphInboxSessionFactory> _sessions = new();
        private readonly Mock<IOffice365GraphInboxSession> _session = new();
        private readonly List<MailBoxSyncCursor> _savedCursors = [];

        public Office365GraphInboxPollerTests()
        {
            _tokens
                .Setup(t => t.GetTokenAsync(It.IsAny<Office365TokenRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync("an-access-token");
            _sessions.Setup(f => f.Create("an-access-token")).Returns(_session.Object);
            _repository
                .Setup(r => r.SaveSyncCursorAsync(It.IsAny<MailBoxSyncCursor>(), It.IsAny<string>()))
                .Callback<MailBoxSyncCursor, string>((cursor, _) => _savedCursors.Add(cursor))
                .Returns(Task.CompletedTask);
            _session
                .Setup(s => s.GetMimeAsync(Mailbox, It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string _, string id, CancellationToken _) => Mime(id));
        }

        private Office365GraphInboxPoller Poller() =>
            new(_sync.Object, _repository.Object, _tokens.Object, _sessions.Object, NullLogger<Office365GraphInboxPoller>.Instance);

        private static MailServerConfiguration Config() => new()
        {
            ItemId = "cfg-in",
            Provider = MailServiceProvider.Office365Smtp,
            IsInbound = true,
            AuthenticationType = MailAuthenticationType.OAuthClientCredentials,
            SecurityMode = MailSecurityMode.SslOnConnect,
            Host = "outlook.office365.com",
            Port = 993,
            TenantId = "contoso-tenant",
            ClientId = "mailer-app",
            ClientSecretReference = "secret-1",
            MailboxAddress = Mailbox,
            IsEnableSnsConfiguration = false
        };

        private static Stream Mime(string id)
        {
            var message = new MimeMessage
            {
                MessageId = $"{id}@contoso.com",
                Subject = $"Subject {id}",
                Body = new TextPart("plain") { Text = "Hello" }
            };
            message.From.Add(new MailboxAddress("Sender", "sender@example.com"));
            message.To.Add(new MailboxAddress("Support", Mailbox));

            var stream = new MemoryStream();
            message.WriteTo(stream);
            stream.Position = 0;
            return stream;
        }

        private static Office365InboxMessage Listed(string id) => new(id, $"<{id}@contoso.com>");

        private void Page(string? cursor, Office365InboxPage page) =>
            _session
                .Setup(s => s.GetInboxDeltaPageAsync(Mailbox, cursor, It.IsAny<CancellationToken>()))
                .ReturnsAsync(page);

        private void StoredCursor(string mailbox, string cursor) =>
            _repository
                .Setup(r => r.GetSyncCursorAsync("cfg-in", "blocks-tenant"))
                .ReturnsAsync(new MailBoxSyncCursor { ConfigurationId = "cfg-in", MailboxAddress = mailbox, Cursor = cursor });

        [Fact]
        public async Task Poll_RequestsAGraphTokenForThePolledTenant()
        {
            Page(null, new Office365InboxPage([], null, DeltaLink));

            await Poller().PollAsync(Config(), "blocks-tenant");

            _tokens.Verify(t => t.GetTokenAsync(
                It.Is<Office365TokenRequest>(r =>
                    r.BlocksTenantId == "blocks-tenant"
                    && r.EntraTenantId == "contoso-tenant"
                    && r.ClientId == "mailer-app"
                    && r.ClientSecretReference == "secret-1"
                    && r.Scope == Office365TokenScopes.Graph),
                It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task Poll_AnInvalidRecord_TouchesNeitherTheVaultNorTheMailbox()
        {
            var config = Config();
            config.AuthenticationType = MailAuthenticationType.Password;

            await Poller().PollAsync(config, "blocks-tenant");

            _tokens.VerifyNoOtherCalls();
            _sessions.VerifyNoOtherCalls();
            _sync.VerifyNoOtherCalls();
        }

        [Fact]
        public async Task Poll_ATokenFailure_IsLoggedNotThrown_AndOpensNoSession()
        {
            _tokens
                .Setup(t => t.GetTokenAsync(It.IsAny<Office365TokenRequest>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new SecretVaultException("down", "Get", "secret-1"));

            var act = () => Poller().PollAsync(Config(), "blocks-tenant");

            await act.Should().NotThrowAsync();
            _sessions.VerifyNoOtherCalls();
        }

        [Fact]
        public async Task FirstRound_StartsWithoutACursor_StoresNewMail_AndSavesTheDeltaLink()
        {
            Page(null, new Office365InboxPage([Listed("m1")], null, DeltaLink));

            await Poller().PollAsync(Config(), "blocks-tenant");

            _sync.Verify(s => s.StoreInboundAsync(
                It.Is<MailServerConfiguration>(c => c.ItemId == "cfg-in"),
                "blocks-tenant",
                It.Is<MimeMessage>(m => m.MessageId == "m1@contoso.com")), Times.Once);

            _savedCursors.Should().ContainSingle().Which.Should().BeEquivalentTo(new
            {
                ConfigurationId = "cfg-in",
                MailboxAddress = Mailbox,
                Cursor = DeltaLink
            });
        }

        [Fact]
        public async Task AnAlreadyStoredMessage_IsNotDownloaded()
        {
            Page(null, new Office365InboxPage([Listed("m1")], null, DeltaLink));
            _repository.Setup(r => r.ExistsAsync("m1@contoso.com", "blocks-tenant")).ReturnsAsync(true);

            await Poller().PollAsync(Config(), "blocks-tenant");

            _session.Verify(s => s.GetMimeAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
            _sync.VerifyNoOtherCalls();
            _savedCursors.Should().ContainSingle();
        }

        [Fact]
        public async Task AMessageWithoutAMessageId_IsSkipped()
        {
            Page(null, new Office365InboxPage([new Office365InboxMessage("m1", null)], null, DeltaLink));

            await Poller().PollAsync(Config(), "blocks-tenant");

            _session.Verify(s => s.GetMimeAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
            _sync.VerifyNoOtherCalls();
        }

        [Fact]
        public async Task ARound_FollowsNextLinks_SavingTheCursorAfterEachPage()
        {
            Page(null, new Office365InboxPage([Listed("m1")], NextLink, null));
            Page(NextLink, new Office365InboxPage([Listed("m2")], null, DeltaLink));

            await Poller().PollAsync(Config(), "blocks-tenant");

            _sync.Verify(s => s.StoreInboundAsync(It.IsAny<MailServerConfiguration>(), "blocks-tenant", It.IsAny<MimeMessage>()), Times.Exactly(2));
            _savedCursors.Select(c => c.Cursor).Should().Equal(NextLink, DeltaLink);
        }

        [Fact]
        public async Task ALaterPoll_ResumesFromTheStoredCursor()
        {
            StoredCursor(Mailbox, DeltaLink);
            Page(DeltaLink, new Office365InboxPage([], null, DeltaLink));

            await Poller().PollAsync(Config(), "blocks-tenant");

            _session.Verify(s => s.GetInboxDeltaPageAsync(Mailbox, DeltaLink, It.IsAny<CancellationToken>()), Times.Once);
            _session.Verify(s => s.GetInboxDeltaPageAsync(Mailbox, null, It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task AnExpiredCursor_RestartsTheRoundOnce()
        {
            StoredCursor(Mailbox, DeltaLink);
            _session
                .Setup(s => s.GetInboxDeltaPageAsync(Mailbox, DeltaLink, It.IsAny<CancellationToken>()))
                .ThrowsAsync(new ODataError { ResponseStatusCode = 410 });
            Page(null, new Office365InboxPage([Listed("m1")], null, DeltaLink));

            await Poller().PollAsync(Config(), "blocks-tenant");

            _sync.Verify(s => s.StoreInboundAsync(It.IsAny<MailServerConfiguration>(), "blocks-tenant", It.IsAny<MimeMessage>()), Times.Once);
            _savedCursors.Should().ContainSingle();
        }

        [Fact]
        public async Task AFailureMidPage_IsLoggedNotThrown_AndKeepsTheCursorWhereItWas()
        {
            Page(null, new Office365InboxPage([Listed("m1"), Listed("m2")], null, DeltaLink));
            _session
                .Setup(s => s.GetMimeAsync(Mailbox, "m2", It.IsAny<CancellationToken>()))
                .ThrowsAsync(new ODataError { ResponseStatusCode = 429 });

            var act = () => Poller().PollAsync(Config(), "blocks-tenant");

            await act.Should().NotThrowAsync();
            _savedCursors.Should().BeEmpty("the page is repeated on the next tick, and storage dedupes m1");
        }

        [Fact]
        public async Task AMessageGoneBeforeItsDownload_IsSkipped_AndTheRoundCompletes()
        {
            Page(null, new Office365InboxPage([Listed("m1"), Listed("m2")], null, DeltaLink));
            _session
                .Setup(s => s.GetMimeAsync(Mailbox, "m1", It.IsAny<CancellationToken>()))
                .ThrowsAsync(new ODataError { ResponseStatusCode = 404 });

            await Poller().PollAsync(Config(), "blocks-tenant");

            _sync.Verify(s => s.StoreInboundAsync(
                It.IsAny<MailServerConfiguration>(),
                "blocks-tenant",
                It.Is<MimeMessage>(m => m.MessageId == "m2@contoso.com")), Times.Once);
            _savedCursors.Should().ContainSingle().Which.Cursor.Should().Be(DeltaLink);
        }

        [Fact]
        public async Task OnePoll_ReadsAtMostTheBoundedNumberOfPages()
        {
            _session
                .Setup(s => s.GetInboxDeltaPageAsync(Mailbox, It.IsAny<string?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new Office365InboxPage([], NextLink, null));

            await Poller().PollAsync(Config(), "blocks-tenant");

            _session.Verify(
                s => s.GetInboxDeltaPageAsync(Mailbox, It.IsAny<string?>(), It.IsAny<CancellationToken>()),
                Times.Exactly(Office365GraphInboxPoller.MaxPagesPerPoll));
        }

        [Theory]
        [InlineData(Mailbox, DeltaLink, DeltaLink)]
        [InlineData("SUPPORT@contoso.com", DeltaLink, DeltaLink)]
        [InlineData("other@contoso.com", DeltaLink, null)]
        [InlineData(Mailbox, "https://attacker.example/v1.0/users/x/mailFolders/inbox/messages/delta", null)]
        [InlineData(Mailbox, "http://graph.microsoft.com/v1.0/delta", null)]
        [InlineData(Mailbox, "", null)]
        public void ResumeFrom_FollowsOnlyAGraphCursorForTheSameMailbox(string storedMailbox, string storedCursor, string? expected)
        {
            var stored = new MailBoxSyncCursor { ConfigurationId = "cfg-in", MailboxAddress = storedMailbox, Cursor = storedCursor };

            Office365GraphInboxPoller.ResumeFrom(stored, Mailbox).Should().Be(expected);
        }

        [Fact]
        public void ResumeFrom_NothingStored_StartsAFreshRound()
        {
            Office365GraphInboxPoller.ResumeFrom(null, Mailbox).Should().BeNull();
        }

        [Theory]
        [InlineData("<abc@contoso.com>", "abc@contoso.com")]
        [InlineData("  <abc@contoso.com>  ", "abc@contoso.com")]
        [InlineData("abc@contoso.com", "abc@contoso.com")]
        [InlineData("<>", null)]
        [InlineData("", null)]
        [InlineData(null, null)]
        public void NormalizeMessageId_MatchesTheFormMimeKitStores(string? input, string? expected)
        {
            Office365GraphInboxPoller.NormalizeMessageId(input).Should().Be(expected);
        }
    }
}
