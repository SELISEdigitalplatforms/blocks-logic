using Blocks.Genesis;
using FluentAssertions;
using FluentValidation;
using FluentValidation.Results;
using Mail.DomainService.Entities;
using Mail.DomainService.Mails;
using Mail.DomainService.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace XUnitTest.Mail
{
    /// <summary>
    /// Who a mail may reach: Send only users (also with phone-as-email), a test mail only the caller.
    /// </summary>
    public sealed class MailRecipientRulesTests : IDisposable
    {
        private readonly Mock<IMailRepository> _repo = new();
        private readonly Mock<IValidator<MailToBeSent>> _validator = new();
        private MailToBeSent? _mapped;

        public MailRecipientRulesTests()
        {
            BlocksContext.SetContext(BlocksContext.Create("t1", [], "caller-user", true, "/api/mail", "org", DateTime.UtcNow.AddMinutes(5),
                "Caller@Test.com", [], "caller", "", "Caller", "token", "t1"));
            _repo.Setup(r => r.GetEmailAdressOfUsers(It.IsAny<IEnumerable<string>>())).ReturnsAsync([]);
            _repo.Setup(r => r.GetPhoneEmailAddressesOfUsers(It.IsAny<IEnumerable<string>>())).ReturnsAsync([]);
            _repo.Setup(r => r.GetUserEmailAsync(It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync((string?)null);
            // Stop right after mapping: the test only needs what would be sent, not the sending.
            _validator.Setup(v => v.ValidateAsync(It.IsAny<MailToBeSent>(), It.IsAny<CancellationToken>()))
                .Callback<MailToBeSent, CancellationToken>((m, _) => _mapped = m)
                .ReturnsAsync(new ValidationResult([new ValidationFailure("x", "stop")]));
        }

        public void Dispose() => BlocksContext.ClearContext();

        private MailService Service() => new(_validator.Object, Mock.Of<IMessageClient>(), _repo.Object,
            Mock.Of<ISendMailService>(), NullLogger<MailService>.Instance);

        // What a console caller really looks like: IAM access tokens carry no email claim.
        private static void SignInWithoutEmailClaim(string tenantId, string userId, bool impersonated = false, string? originalTenantId = null)
        {
            BlocksContext.SetContext(BlocksContext.Create(tenantId: tenantId, roles: [], userId: userId, isAuthenticated: true,
                requestUri: "/api/mail", organizationId: "default", expireOn: DateTime.UtcNow.AddMinutes(5), email: "",
                permissions: [], userName: "", phoneNumber: "", displayName: "", oauthToken: "token",
                originalTenantId: originalTenantId ?? tenantId, applicationDomain: "", impersonated: impersonated));
        }

        [Theory]
        [InlineData("someone@else.com", null)]
        [InlineData("caller@test.com", "someone@else.com")]
        public async Task TestMail_ToAnyoneButTheCaller_IsRefused_AndNothingIsMapped(string to, string? bcc)
        {
            var response = await Service().ProcessMailToAnyAsync(new SendMailToAny
            {
                IsTestMail = true, Purpose = "p", To = [to], Bcc = bcc is null ? [] : [bcc]
            });

            response.IsSuccess.Should().BeFalse();
            response.Errors.Should().ContainKey("To");
            _mapped.Should().BeNull();
        }

        [Fact]
        public async Task TestMail_ToTheCallersOwnAddress_GoesThrough()
        {
            await Service().ProcessMailToAnyAsync(new SendMailToAny { IsTestMail = true, Purpose = "p", To = [" caller@test.com "] });

            _mapped.Should().NotBeNull();
            _mapped!.IsTestMail.Should().BeTrue();
            _repo.Verify(r => r.GetUserEmailAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task TestMail_WithAnIamToken_ReadsTheCallersAddressFromTheirUserRecord()
        {
            SignInWithoutEmailClaim("t1", "user-1");
            _repo.Setup(r => r.GetUserEmailAsync("t1", "user-1")).ReturnsAsync("Me@App.com");

            await Service().ProcessMailToAnyAsync(new SendMailToAny { IsTestMail = true, Purpose = "p", To = [" me@app.com "] });

            _mapped.Should().NotBeNull();
            _mapped!.IsTestMail.Should().BeTrue();
        }

        [Fact]
        public async Task TestMail_WithAnIamToken_ToSomeoneElse_IsRefused()
        {
            SignInWithoutEmailClaim("t1", "user-1");
            _repo.Setup(r => r.GetUserEmailAsync("t1", "user-1")).ReturnsAsync("me@app.com");

            var response = await Service().ProcessMailToAnyAsync(new SendMailToAny
            {
                IsTestMail = true, Purpose = "p", To = ["me@app.com"], Cc = ["someone@else.com"]
            });

            response.Errors.Should().ContainKey("To");
            _mapped.Should().BeNull();
        }

        [Fact]
        public async Task TestMail_Impersonated_ReadsTheCallerFromTheRootTenant()
        {
            SignInWithoutEmailClaim("project-1", "root-user", impersonated: true, originalTenantId: "root-1");
            _repo.Setup(r => r.GetUserEmailAsync("root-1", "root-user")).ReturnsAsync("me@root.com");

            await Service().ProcessMailToAnyAsync(new SendMailToAny { IsTestMail = true, Purpose = "p", To = ["me@root.com"] });

            _mapped.Should().NotBeNull();
            _repo.Verify(r => r.GetUserEmailAsync("project-1", It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task TestMail_WithAnIamToken_AndNoUserRecord_IsRefused()
        {
            SignInWithoutEmailClaim("t1", "gone-user");

            var response = await Service().ProcessMailToAnyAsync(new SendMailToAny { IsTestMail = true, Purpose = "p", To = ["me@app.com"] });

            response.Errors.Should().ContainKey("To");
            _mapped.Should().BeNull();
        }

        [Fact]
        public async Task TestMail_WithNoSignedInEmail_IsRefused()
        {
            BlocksContext.SetContext(BlocksContext.Create("t1", [], "svc", true, "/api/mail", "org", DateTime.UtcNow.AddMinutes(5),
                "", [], "svc", "", "", "token", "t1"));

            var response = await Service().ProcessMailToAnyAsync(new SendMailToAny { IsTestMail = true, Purpose = "p", To = [""] });

            response.IsSuccess.Should().BeFalse();
        }

        [Fact]
        public async Task SendToAny_NotATest_KeepsEveryRecipient()
        {
            await Service().ProcessMailToAnyAsync(new SendMailToAny { Purpose = "p", To = ["customer@shop.com"] });

            _mapped!.To.Should().Equal("customer@shop.com");
            _repo.Verify(r => r.GetEmailAdressOfUsers(It.IsAny<IEnumerable<string>>()), Times.Never);
        }

        [Fact]
        public async Task Send_PhoneAsEmail_KeepsOnlyAddressesOfUsersPhones()
        {
            _repo.Setup(r => r.GetPhoneEmailAddressesOfUsers(It.Is<IEnumerable<string>>(a => a.Contains("008801711000000@sms.gw"))))
                .ReturnsAsync(["008801711000000@sms.gw"]);

            await Service().ProcessMailAsync(new SendMail
            {
                SendPhoneNumberAsEmail = true,
                Purpose = "p",
                To = ["008801711000000@sms.gw", "anyone@evil.example"],
                Cc = ["boss@evil.example"]
            });

            _mapped!.To.Should().Equal("008801711000000@sms.gw");
            _mapped.Cc.Should().BeEmpty();
            _repo.Verify(r => r.GetEmailAdressOfUsers(It.IsAny<IEnumerable<string>>()), Times.Never);
        }

        [Fact]
        public async Task Send_Normal_StillResolvesUsersByEmail()
        {
            _repo.Setup(r => r.GetEmailAdressOfUsers(It.Is<IEnumerable<string>>(a => a.Contains("user@app.com")))).ReturnsAsync(["user@app.com"]);

            await Service().ProcessMailAsync(new SendMail { Purpose = "p", To = ["user@app.com", "stranger@evil.example"] });

            _mapped!.To.Should().Equal("user@app.com");
        }

        [Theory]
        [InlineData("008801711000000@sms.gw", "+880 1711-000000", true)]
        [InlineData("008801711000000@sms.gw", "008801711000000", true)]
        [InlineData("01711000000@sms.gw", "01711000000", true)]
        [InlineData("008801711000000@sms.gw", "+880 1711 000001", false)]
        [InlineData("008801711000000@sms.gw", "+880 1711 0000001", false)]
        public void PhoneEmail_MatchesTheStoredNumberInAnyCommonFormat(string address, string stored, bool matches)
        {
            var pattern = PhoneEmail.UserPhonePattern(address);

            pattern.Should().NotBeNull();
            System.Text.RegularExpressions.Regex.IsMatch(stored, pattern!).Should().Be(matches);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("no-at-sign")]
        [InlineData("@sms.gw")]
        [InlineData("name@evil.example")]
        [InlineData("12.4@sms.gw")]
        [InlineData("0012@sms.gw")]
        [InlineData(".*@sms.gw")]
        public void PhoneEmail_WithoutAPhonePart_HasNoPattern(string? address)
        {
            PhoneEmail.UserPhonePattern(address).Should().BeNull();
        }
    }
}
