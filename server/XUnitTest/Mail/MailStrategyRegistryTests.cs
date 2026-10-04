using FluentAssertions;
using Mail.DomainService.Mails.Strategies;
using Mail.DomainService.Shared.Enums;
using Microsoft.Extensions.Logging.Abstractions;
using Mail.DomainService.Mails;
using Moq;

namespace XUnitTest.Mail
{
    /// <summary>
    /// H2 and C6: dispatch by provider, and an unregistered combination refused before anything
    /// is resolved or connected.
    /// </summary>
    public class MailStrategyRegistryTests
    {
        private static SmtpClientProvider SmtpProvider() =>
            new(new Mock<IServiceProvider>().Object, NullLogger<SmtpClientProvider>.Instance);

        private static OutboundMailSenderRegistry Outbound(params IOutboundMailSender[] senders) =>
            new(senders.Length > 0
                ? senders
                : [new AmazonSesMailSender(SmtpProvider()), new ZohoMailSender(SmtpProvider())]);

        [Theory]
        [InlineData(MailServiceProvider.AmazonSes)]
        [InlineData(MailServiceProvider.Zoho)]
        public void TryResolve_SelectsTheLegacySenderForEachPasswordProvider(MailServiceProvider provider)
        {
            Outbound().TryResolve(provider, out var sender).Should().BeTrue();

            sender.Provider.Should().Be(provider);
            sender.Should().BeAssignableTo<LegacySmtpSender>();
        }

        [Fact]
        public void TryResolve_UnregisteredProvider_ReportsAMissRatherThanThrowing()
        {
            // The caller answers with the existing failure event; an exception here would escape
            // the send-result path and take the consumer down instead of reporting the send.
            Outbound().TryResolve(MailServiceProvider.Office365Smtp, out var sender).Should().BeFalse();
            sender.Should().BeNull();
        }

        [Fact]
        public void TryResolve_UndefinedProviderValue_IsAMissToo()
        {
            // An undefined numeric value deserializes onto the enum unchallenged.
            Outbound().TryResolve((MailServiceProvider)42, out _).Should().BeFalse();
        }

        [Fact]
        public void LegacySenders_DoNotClaimToDiagnoseTheirOwnFailures()
        {
            // Their logs must not change, so the orchestrator keeps emitting its summary for them.
            ((IOutboundMailSender)new AmazonSesMailSender(SmtpProvider())).EmitsOwnFailureDiagnostic.Should().BeFalse();
            ((IOutboundMailSender)new ZohoMailSender(SmtpProvider())).EmitsOwnFailureDiagnostic.Should().BeFalse();
        }

        [Fact]
        public void ANewlyRegisteredSender_IsSelectedWithoutTouchingTheOrchestrator()
        {
            var stub = new Mock<IOutboundMailSender>();
            stub.SetupGet(s => s.Provider).Returns(MailServiceProvider.Office365Smtp);

            var registry = Outbound(stub.Object);

            registry.TryResolve(MailServiceProvider.Office365Smtp, out var resolved).Should().BeTrue();
            resolved.Should().BeSameAs(stub.Object);
            registry.RegisteredProviders.Should().BeEquivalentTo([MailServiceProvider.Office365Smtp]);
        }

        [Fact]
        public void InboundRegistry_HasNoOffice365Poller()
        {
            var poller = new Mock<IInboundMailPoller>();
            poller.SetupGet(p => p.Provider).Returns(MailServiceProvider.Zoho);
            poller.SetupGet(p => p.Mode).Returns(InboundMailMode.Poll);

            var registry = new InboundMailPollerRegistry([poller.Object]);

            registry.TryResolve(MailServiceProvider.Zoho, out _).Should().BeTrue();
            registry.TryResolve(MailServiceProvider.Office365Smtp, out var missing).Should().BeFalse();
            missing.Should().BeNull();
        }
    }
}
