using FluentAssertions;
using Mail.DomainService.Mails;
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

        [Fact]
        public void TheMailPasswordResolver_IsRegisteredOnce_AndResolvesAsASingleton()
        {
            // #371: RegisterAllMailApplicationServices runs more than once in the Api and the worker.
            var services = new ServiceCollection();
            services.AddLogging();
            services.RegisterAllMailApplicationServices();
            services.RegisterAllMailApplicationServices();

            services.Where(d => d.ServiceType == typeof(IMailPasswordResolver)).Should().ContainSingle()
                .Which.Lifetime.Should().Be(ServiceLifetime.Singleton);
            using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
            provider.GetRequiredService<IMailPasswordResolver>().Should().BeOfType<MailPasswordResolver>();
        }

        [Fact]
        public void SendMailService_ResolvesWithItsGroupedResolvers_RegisteredOnceAsASingleton()
        {
            // Sonar S107: SendMailService takes its attachment and password lookups as one
            // MailSendResolvers; every host that registers it calls RegisterAllMailApplicationServices.
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton(Moq.Mock.Of<Blocks.Genesis.IMessageClient>());
            services.AddSingleton<Microsoft.Extensions.Configuration.IConfiguration>(new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build());
            services.RegisterAllMailApplicationServices();
            services.RegisterAllMailApplicationServices();
            services.AddSingleton(Moq.Mock.Of<global::Mail.DomainService.Services.IMailRepository>()); // no database here

            services.Where(d => d.ServiceType == typeof(MailSendResolvers)).Should().ContainSingle()
                .Which.Lifetime.Should().Be(ServiceLifetime.Singleton);
            using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
            var resolvers = provider.GetRequiredService<MailSendResolvers>();
            resolvers.Password.Should().BeSameAs(provider.GetRequiredService<IMailPasswordResolver>());
            resolvers.Attachments.Should().BeSameAs(provider.GetRequiredService<IMailAttachmentResolver>());
            provider.GetRequiredService<ISendMailService>().Should().BeOfType<SendMailService>();
        }

        [Fact]
        public void MailSendResolvers_RejectsAMissingResolver()
        {
            var attachments = Moq.Mock.Of<IMailAttachmentResolver>();
            var password = Moq.Mock.Of<IMailPasswordResolver>();

            ((Action)(() => _ = new MailSendResolvers(null!, password))).Should().Throw<ArgumentNullException>();
            ((Action)(() => _ = new MailSendResolvers(attachments, null!))).Should().Throw<ArgumentNullException>();
        }

        [Fact]
        public void TheInboxHostRegistration_AloneProvidesTheMailPasswordResolver()
        {
            // MailBoxSyncService/Program.cs calls only RegisterOffice365TokenServices.
            var services = new ServiceCollection();
            services.RegisterOffice365TokenServices();

            using var provider = services.BuildServiceProvider();
            provider.GetRequiredService<IMailPasswordResolver>().Should().BeOfType<MailPasswordResolver>();
        }

        [Fact]
        public void TheServicesHostRegistration_ProvidesTheConnectionStringResolverOnce()
        {
            var services = new ServiceCollection();
            DomainService.Shared.ApplicationServiceCollectionExtensions.AddApplicationServices(services);
            DomainService.Shared.ApplicationServiceCollectionExtensions.AddApplicationServices(services);

            services.Where(d => d.ServiceType == typeof(DomainService.ManagedService.Services.IServiceBusConnectionStringResolver))
                .Should().ContainSingle()
                .Which.Lifetime.Should().Be(ServiceLifetime.Singleton);
        }
    }
}
