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
    /// hash of the request tenant id with that tenant's salt, OR its token has the send permission. Enforce (the
    /// default) refuses any other call, Report lets it through and logs it, Off does not check; the secret itself
    /// is never logged.
    /// </summary>
    public sealed class NotifierSendGuardTests : IDisposable
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

        private int _tokenChecks;

        private Func<Task<NotifierCallerToken>> Token(NotifierCallerToken token) => () =>
        {
            _tokenChecks++;
            return Task.FromResult(token);
        };

        private static Task<bool> Allowed(NotifierSendGuard sut, string? secret) =>
            sut.IsAllowedAsync(secret, () => Task.FromResult(NotifierCallerToken.None), SendAction);

        [Theory]
        [InlineData("Off")]
        [InlineData("Report")]
        [InlineData("Enforce")]
        public async Task IsAllowed_AcceptsTheHashOfTheTenantAndItsSalt(string mode)
        {
            (await Build(mode).IsAllowedAsync(GoodSecret, Token(NotifierCallerToken.None), SendAction)).Should().BeTrue();

            _logger.Lines.Should().BeEmpty();
        }

        [Fact]
        public async Task IsAllowed_RightSecretNeedsNoTokenCheck()
        {
            (await Build(EnforceMode).IsAllowedAsync(GoodSecret, Token(NotifierCallerToken.Invalid), SendAction))
                .Should().BeTrue();

            _tokenChecks.Should().Be(0);
        }

        [Fact]
        public async Task IsAllowed_DoesNotCheckAnythingWhenOff()
        {
            TestBlocksContext.Clear();

            (await Build("Off").IsAllowedAsync(null, Token(NotifierCallerToken.Invalid), SendAction)).Should().BeTrue();

            _tenants.Verify(t => t.GetTenantByID(It.IsAny<string>()), Times.Never);
            _tokenChecks.Should().Be(0);
            _logger.Lines.Should().BeEmpty();
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        public async Task IsAllowed_ReportLetsAMissingSecretThroughAndLogsIt(string? secret)
        {
            (await Allowed(Build(ReportMode), secret)).Should().BeTrue();

            _logger.All.Should().Contain("missing").And.Contain(TenantId);
        }

        [Fact]
        public async Task IsAllowed_EnforceRefusesNoSecretAndNoToken()
        {
            (await Build(EnforceMode).IsAllowedAsync(null, Token(NotifierCallerToken.None), SendAction)).Should().BeFalse();

            _tokenChecks.Should().Be(1);
            _logger.All.Should().Contain("missing").And.Contain(nameof(NotifierCallerToken.None));
        }

        [Fact]
        public async Task IsAllowed_EnforceRefusesAValidTokenWithoutThePermission()
        {
            (await Build(EnforceMode).IsAllowedAsync(null, Token(NotifierCallerToken.NoPermission), SendAction))
                .Should().BeFalse();

            _logger.All.Should().Contain(nameof(NotifierCallerToken.NoPermission));
        }

        [Theory]
        [InlineData(null)]
        [InlineData(BadSecret)]
        public async Task IsAllowed_EnforceAllowsAValidTokenWithThePermission(string? secret)
        {
            (await Build(EnforceMode).IsAllowedAsync(secret, Token(NotifierCallerToken.Permitted), SendAction))
                .Should().BeTrue();

            _logger.Lines.Should().BeEmpty();
        }

        [Fact]
        public async Task IsAllowed_EnforceAllowsAPermittedTokenWithoutATenantSecret()
        {
            TestBlocksContext.Clear();

            (await Build(EnforceMode).IsAllowedAsync(null, Token(NotifierCallerToken.Permitted), SendAction))
                .Should().BeTrue();
        }

        [Fact]
        public async Task IsAllowed_EnforceRefusesAnInvalidOrExpiredToken()
        {
            (await Build(EnforceMode).IsAllowedAsync(null, Token(NotifierCallerToken.Invalid), SendAction))
                .Should().BeFalse();

            _logger.All.Should().Contain(nameof(NotifierCallerToken.Invalid));
        }

        [Fact]
        public async Task IsAllowed_ReportLetsAnInvalidTokenThroughAndLogsIt()
        {
            (await Build(ReportMode).IsAllowedAsync(BadSecret, Token(NotifierCallerToken.Invalid), SendAction))
                .Should().BeTrue();

            _logger.Lines.Should().ContainSingle();
            _logger.All.Should().Contain("mismatch").And.Contain(nameof(NotifierCallerToken.Invalid));
        }

        [Fact]
        public async Task IsAllowed_EnforceRefusesACallWithoutATenant()
        {
            TestBlocksContext.Clear();

            (await Allowed(Build(EnforceMode), GoodSecret)).Should().BeFalse();

            _logger.All.Should().Contain("no tenant");
        }

        [Fact]
        public async Task IsAllowed_EnforceRefusesAnEmptyTenantId()
        {
            TestBlocksContext.Set("");

            (await Allowed(Build(EnforceMode), GoodSecret)).Should().BeFalse();

            _logger.All.Should().Contain("no tenant");
        }

        [Fact]
        public async Task IsAllowed_EnforceRefusesAnUnknownTenant()
        {
            _tenants.Setup(t => t.GetTenantByID(TenantId)).Returns((Tenant?)null);

            (await Allowed(Build(EnforceMode), GoodSecret)).Should().BeFalse();

            _logger.All.Should().Contain("unknown tenant");
        }

        [Fact]
        public async Task IsAllowed_EnforceRefusesATenantWithoutASalt()
        {
            _tenants.Setup(t => t.GetTenantByID(TenantId)).Returns(NewTenant(""));

            (await Allowed(Build(EnforceMode), GoodSecret)).Should().BeFalse();

            _logger.All.Should().Contain("unknown tenant");
            _crypto.Verify(c => c.ConstantTimeEquals(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task IsAllowed_EnforceRefusesAWrongSecret()
        {
            (await Allowed(Build(EnforceMode), BadSecret)).Should().BeFalse();

            _logger.All.Should().Contain("mismatch");
        }

        [Fact]
        public async Task IsAllowed_ReportLetsAWrongSecretThroughAndLogsTheActionAndTenant()
        {
            (await Allowed(Build(ReportMode), BadSecret)).Should().BeTrue();

            _logger.All.Should().Contain(SendAction).And.Contain(TenantId).And.Contain(ReportMode);
        }

        [Theory]
        [InlineData("Report")]
        [InlineData("Enforce")]
        public async Task IsAllowed_NeverLogsTheSecretOrItsExpectedHash(string mode)
        {
            await Allowed(Build(mode), BadSecret);
            await Allowed(Build(mode), null);

            _logger.Lines.Should().NotBeEmpty();
            _logger.All.Should().NotContain(BadSecret).And.NotContain(GoodSecret);
        }

        [Fact]
        public async Task IsAllowed_RejectsANullTokenCheck()
        {
            var act = () => Build(EnforceMode).IsAllowedAsync(null, null!, SendAction);

            await act.Should().ThrowAsync<ArgumentNullException>();
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("Strict")]
        [InlineData("7")]
        public async Task IsAllowed_TreatsAMissingOrUnknownModeAsEnforce(string? mode)
        {
            var sut = Build(mode);

            sut.Mode.Should().Be(NotifierSendAuthMode.Enforce);
            (await Allowed(sut, BadSecret)).Should().BeFalse();
            _logger.Lines.Should().ContainSingle();
        }

        [Theory]
        [InlineData("off", NotifierSendAuthMode.Off)]
        [InlineData("REPORT", NotifierSendAuthMode.Report)]
        [InlineData("enforce", NotifierSendAuthMode.Enforce)]
        public void Mode_IgnoresTheCaseOfTheConfiguredValue(string value, NotifierSendAuthMode expected)
        {
            Build(value).Mode.Should().Be(expected);
        }
    }
}
