using Blocks.Genesis;
using DomainService.Notification;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Moq;
using XUnitTest.TestHelpers;

namespace XUnitTest.Notification
{
    /// <summary>
    /// Unit tests for <see cref="NotifierSendGuard"/>. A call is valid when its <c>Secret</c> header equals the
    /// hash of the request tenant id with that tenant's salt. Report lets an invalid call through and logs it,
    /// Enforce refuses it, Off does not check; the secret itself is never logged.
    /// </summary>
    public class NotifierSendGuardTests : IDisposable
    {
        private const string TenantId = "tenant-a";
        private const string Salt = "salt-a";
        private const string GoodSecret = "good-secret-value";
        private const string BadSecret = "bad-secret-value";
        private const string SendAction = "Notify";
        private const string ReportMode = "Report";
        private const string EnforceMode = "Enforce";

        private readonly Mock<ITenants> _tenants = new();
        private readonly Mock<ICryptoService> _crypto = new();
        private readonly CapturingLogger<NotifierSendGuard> _logger = new();

        public NotifierSendGuardTests()
        {
            _tenants.Setup(t => t.GetTenantByID(TenantId)).Returns(NewTenant(Salt));
            _crypto.Setup(c => c.Hash(TenantId, Salt, It.IsAny<bool>())).Returns(GoodSecret);
            _crypto.Setup(c => c.ConstantTimeEquals(It.IsAny<string>(), It.IsAny<string>()))
                   .Returns((string a, string b) => a == b);
            TestBlocksContext.Set(TenantId);
        }

        public void Dispose()
        {
            TestBlocksContext.Clear();
            GC.SuppressFinalize(this);
        }

        private static Tenant NewTenant(string salt) => new()
        {
            TenantId = TenantId,
            TenantSalt = salt,
            DbConnectionString = "conn",
            JwtTokenParameters = new JwtTokenParameters { PrivateCertificatePassword = "x", IssueDate = DateTime.UtcNow },
        };

        private NotifierSendGuard Build(string? mode)
        {
            var settings = new Dictionary<string, string?>();
            if (mode is not null) settings[NotifierSendGuard.ModeConfigKey] = mode;
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
            return new NotifierSendGuard(configuration, _tenants.Object, _crypto.Object, _logger);
        }

        [Theory]
        [InlineData("Off")]
        [InlineData("Report")]
        [InlineData("Enforce")]
        public void IsAllowed_AcceptsTheHashOfTheTenantAndItsSalt(string mode)
        {
            Build(mode).IsAllowed(GoodSecret, SendAction).Should().BeTrue();

            _logger.Lines.Should().BeEmpty();
        }

        [Fact]
        public void IsAllowed_DoesNotCheckAnythingWhenOff()
        {
            TestBlocksContext.Clear();

            Build("Off").IsAllowed(null, SendAction).Should().BeTrue();

            _tenants.Verify(t => t.GetTenantByID(It.IsAny<string>()), Times.Never);
            _logger.Lines.Should().BeEmpty();
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        public void IsAllowed_ReportLetsAMissingSecretThroughAndLogsIt(string? secret)
        {
            Build(ReportMode).IsAllowed(secret, SendAction).Should().BeTrue();

            _logger.All.Should().Contain("missing").And.Contain(TenantId);
        }

        [Fact]
        public void IsAllowed_EnforceRefusesAMissingSecret()
        {
            Build(EnforceMode).IsAllowed(null, SendAction).Should().BeFalse();
        }

        [Fact]
        public void IsAllowed_EnforceRefusesACallWithoutATenant()
        {
            TestBlocksContext.Clear();

            Build(EnforceMode).IsAllowed(GoodSecret, SendAction).Should().BeFalse();

            _logger.All.Should().Contain("no tenant");
        }

        [Fact]
        public void IsAllowed_EnforceRefusesAnEmptyTenantId()
        {
            TestBlocksContext.Set("");

            Build(EnforceMode).IsAllowed(GoodSecret, SendAction).Should().BeFalse();

            _logger.All.Should().Contain("no tenant");
        }

        [Fact]
        public void IsAllowed_EnforceRefusesAnUnknownTenant()
        {
            _tenants.Setup(t => t.GetTenantByID(TenantId)).Returns((Tenant?)null);

            Build(EnforceMode).IsAllowed(GoodSecret, SendAction).Should().BeFalse();

            _logger.All.Should().Contain("unknown tenant");
        }

        [Fact]
        public void IsAllowed_EnforceRefusesATenantWithoutASalt()
        {
            _tenants.Setup(t => t.GetTenantByID(TenantId)).Returns(NewTenant(""));

            Build(EnforceMode).IsAllowed(GoodSecret, SendAction).Should().BeFalse();

            _logger.All.Should().Contain("unknown tenant");
            _crypto.Verify(c => c.ConstantTimeEquals(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public void IsAllowed_EnforceRefusesAWrongSecret()
        {
            Build(EnforceMode).IsAllowed(BadSecret, SendAction).Should().BeFalse();

            _logger.All.Should().Contain("mismatch");
        }

        [Fact]
        public void IsAllowed_ReportLetsAWrongSecretThroughAndLogsTheActionAndTenant()
        {
            Build(ReportMode).IsAllowed(BadSecret, SendAction).Should().BeTrue();

            _logger.All.Should().Contain(SendAction).And.Contain(TenantId).And.Contain(ReportMode);
        }

        [Theory]
        [InlineData("Report")]
        [InlineData("Enforce")]
        public void IsAllowed_NeverLogsTheSecretOrItsExpectedHash(string mode)
        {
            Build(mode).IsAllowed(BadSecret, SendAction);
            Build(mode).IsAllowed(null, SendAction);

            _logger.Lines.Should().NotBeEmpty();
            _logger.All.Should().NotContain(BadSecret).And.NotContain(GoodSecret);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("Strict")]
        [InlineData("7")]
        public void IsAllowed_TreatsAMissingOrUnknownModeAsReport(string? mode)
        {
            var sut = Build(mode);

            sut.Mode.Should().Be(NotifierSendAuthMode.Report);
            sut.IsAllowed(BadSecret, SendAction).Should().BeTrue();
            _logger.Lines.Should().ContainSingle();
        }

        [Fact]
        public void Mode_IgnoresTheCaseOfTheConfiguredValue()
        {
            Build("enforce").Mode.Should().Be(NotifierSendAuthMode.Enforce);
        }
    }
}
