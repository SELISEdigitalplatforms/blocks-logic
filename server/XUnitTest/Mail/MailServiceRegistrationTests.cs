using FluentAssertions;
using Mail.DomainService.Mails.Office365;
using Mail.DomainService.Mails.Strategies;
using Mail.DomainService.Shared.Utilities;
using Microsoft.Extensions.DependencyInjection;

namespace XUnitTest.Mail
{
    /// <summary>
    /// The API and the worker both invoke <see cref="ApplicationServiceCollectionExtensions.RegisterAllMailApplicationServices"/>
    /// more than once. A second <see cref="IOutboundMailSender"/> for the same provider makes
    /// <see cref="OutboundMailSenderRegistry"/> throw while the host builds a controller.
    /// </summary>
    public class MailServiceRegistrationTests
    {
        [Fact]
        public void RegisterAllMailApplicationServices_CalledTwice_RegistersEachOutboundSenderOnce()
        {
            var services = new ServiceCollection();
            services.RegisterAllMailApplicationServices();
            services.RegisterAllMailApplicationServices();

            var senders = services.Where(d => d.ServiceType == typeof(IOutboundMailSender)).ToList();

            senders.Should().HaveCount(4);
            senders.Select(d => d.ImplementationType).Should().BeEquivalentTo(
            [
                typeof(AmazonSesMailSender),
                typeof(ZohoMailSender),
                typeof(Office365MailSender),
                typeof(GmailMailSender)
            ]);
            services.Count(d => d.ServiceType == typeof(IOutboundMailSenderRegistry)).Should().Be(1);
        }

        [Fact]
        public void TheOffice365Sender_ResolvesWithBothTransports()
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.RegisterAllMailApplicationServices();

            using var provider = services.BuildServiceProvider();

            provider.GetServices<IOutboundMailSender>().OfType<Office365MailSender>().Should().ContainSingle();
            provider.GetRequiredService<Office365GraphMailSender>().Should().NotBeNull();
            provider.GetRequiredService<Office365SmtpClient>().Should().NotBeNull();
        }
    }
}
