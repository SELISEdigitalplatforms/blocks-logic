using Blocks.Genesis;
using FluentAssertions;
using Mail.DomainService.Entities;
using Mail.DomainService.Mails;
using MailKit.Security;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using MimeKit;

namespace XUnitTest.Mail
{
    /// <summary>
    /// The SES headers the legacy SMTP senders add when a configuration sends them. The body is
    /// never one of them: Exchange Online rejects a header value over 32,768 characters
    /// (554 5.6.211), so a body copied into a header bounced every large template.
    /// </summary>
    public class SesHeaderTests : IDisposable
    {
        private const string TenantId = "tenant-ses-headers";
        private const string ConfigurationSet = "blocks-config-set";
        private const int ExchangeHeaderValueLimit = 32_768;

        private readonly BlocksContext? _previousContext;

        public SesHeaderTests()
        {
            _previousContext = BlocksContext.GetContext();
            BlocksContext.SetContext(
                BlocksContext.Create(TenantId, [], "user", true, "", "", DateTime.MaxValue, "", [], "", "", "", "", "", TenantId),
                true);
        }

        public void Dispose() => BlocksContext.SetContext(_previousContext!, _previousContext is not null);

        private static IConfiguration Configuration() =>
            new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { ["SnsConfigurationName"] = ConfigurationSet })
                .Build();

        private static MailToBeSent AMail(bool? sesHeaders = true) => new()
        {
            ItemId = "mail-1",
            To = ["recipient@example.com"],
            MailServerConfiguration = new MailServerConfiguration
            {
                ItemId = "cfg-1",
                Host = "email-smtp.eu-central-1.amazonaws.com",
                Port = 587,
                SenderUserName = "user",
                AccountPassword = "password",
                SenderName = "Blocks",
                SenderAddress = "blocks@example.com",
                IsEnableSnsConfiguration = sesHeaders
            }
        };

        private static MailBody ABody(string body) => new()
        {
            Subject = "Welcome",
            Body = body,
            Attachments = []
        };

        // A welcome template measured in production at 50,506 characters.
        private static string ALargeBody() => "<html><body>" + new string('x', 50_000) + "</body></html>";

        [Theory]
        [InlineData(true)]
        [InlineData(null)]
        public async Task MailKit_SendsSesHeaders_ButNotTheBody(bool? sesHeaders)
        {
            var client = new RecordingMailKitClient(Configuration());

            var sent = await client.SendAsync(AMail(sesHeaders), ABody("<p>Hello</p>"));

            sent.Should().BeTrue();
            var headers = client.Sent!.Headers;
            headers["X-SES-CONFIGURATION-SET"].Should().Be(ConfigurationSet);
            headers["X-Tenant-Id"].Should().Be(TenantId);
            headers.Contains("X-Mail-Body").Should().BeFalse();
        }

        [Fact]
        public async Task MailKit_LargeBody_KeepsEveryHeaderUnderTheExchangeLimit()
        {
            var client = new RecordingMailKitClient(Configuration());
            var body = ALargeBody();

            var sent = await client.SendAsync(AMail(), ABody(body));

            sent.Should().BeTrue();
            client.Sent!.HtmlBody.Should().Be(body);
            client.Sent.Headers.Should().OnlyContain(h => h.Value.Length < ExchangeHeaderValueLimit);
        }

        [Fact]
        public async Task MailKit_SesHeadersOff_SendsNoSesHeaders()
        {
            var client = new RecordingMailKitClient(Configuration());

            await client.SendAsync(AMail(sesHeaders: false), ABody("<p>Hello</p>"));

            var headers = client.Sent!.Headers;
            headers.Contains("X-SES-CONFIGURATION-SET").Should().BeFalse();
            headers.Contains("X-Tenant-Id").Should().BeFalse();
            headers.Contains("X-Mail-Body").Should().BeFalse();
        }

        [Fact]
        public async Task SystemNetMail_SendsSesHeaders_ButNotTheBody()
        {
            var client = new RecordingNetClient(Configuration());

            var sent = await client.SendAsync(AMail(), ABody(ALargeBody()));

            sent.Should().BeTrue();
            client.Headers.Should().ContainKey("X-SES-CONFIGURATION-SET").WhoseValue.Should().Be(ConfigurationSet);
            client.Headers.Should().ContainKey("X-Tenant-Id").WhoseValue.Should().Be(TenantId);
            client.Headers.Should().NotContainKey("X-Mail-Body");
        }

        /// <summary>A real <see cref="MailKitSmtpClient"/> with only the socket swapped out.</summary>
        private sealed class RecordingMailKitClient(IConfiguration configuration)
            : MailKitSmtpClient(NullLogger<MailKitSmtpClient>.Instance, configuration)
        {
            public MimeMessage? Sent { get; private set; }

            protected override IMailKitSmtpClient CreateSmtpClient() => new Session(this);

            private sealed class Session(RecordingMailKitClient owner) : IMailKitSmtpClient
            {
                public SmtpConnectionDiagnostics Diagnostics { get; } = new(TimeProvider.System);

                public Task ConnectAsync(string host, int port, SecureSocketOptions socketOptions, CancellationToken cancellationToken) => Task.CompletedTask;
                public Task AuthenticateAsync(string userName, string password, CancellationToken cancellationToken) => Task.CompletedTask;
                public Task AuthenticateAsync(SaslMechanism mechanism) => Task.CompletedTask;
                public Task DisconnectAsync(bool quit) => Task.CompletedTask;
                public void Dispose() { }

                public Task SendAsync(MimeMessage message)
                {
                    owner.Sent = message;
                    return Task.CompletedTask;
                }
            }
        }

        /// <summary>
        /// A real <see cref="MicrosoftSmtpClient"/> with the System.Net client swapped out. The
        /// headers are copied while sending, because the sender disposes the message afterwards.
        /// </summary>
        private sealed class RecordingNetClient(IConfiguration configuration)
            : MicrosoftSmtpClient(NullLogger<MicrosoftSmtpClient>.Instance, configuration)
        {
            public Dictionary<string, string?> Headers { get; } = new(StringComparer.OrdinalIgnoreCase);

            protected override INetSmtpClient CreateSmtpClient(MailServerConfiguration config) => new Sink(this);

            private sealed class Sink(RecordingNetClient owner) : INetSmtpClient
            {
                public Task SendMailAsync(System.Net.Mail.MailMessage message)
                {
                    foreach (string key in message.Headers.Keys)
                    {
                        owner.Headers[key] = message.Headers[key];
                    }

                    return Task.CompletedTask;
                }

                public void Dispose() { }
            }
        }
    }
}
