using Blocks.Genesis;
using Mail.DomainService.Entities;
using MailBoxSyncService.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;

namespace XUnitTest.MailBoxSyncService
{
    public class WorkerTests
    {
        private sealed class TestWorker : global::MailBoxSyncService.Worker
        {
            public TestWorker(
                IMailRepository mailRepository,
                IMailBoxSyncService mailBoxSyncService,
                ILogger<global::MailBoxSyncService.Worker> logger,
                IConfiguration configuration)
                : base(mailRepository, mailBoxSyncService, logger, configuration)
            {
            }

            public Task RunAsync(CancellationToken token) => ExecuteAsync(token);
        }

        [Fact]
        public async Task ExecuteAsync_Should_Sync_Inbox_For_Tenants()
        {
            var mailRepository = new Mock<IMailRepository>();
            var syncService = new Mock<IMailBoxSyncService>();
            var logger = new Mock<ILogger<global::MailBoxSyncService.Worker>>();
            var tenant = CreateTenant();
            var config = new MailServerConfiguration { ItemId = "config-1", IsInbound = true };

            mailRepository.Setup(r => r.GetTenantsAsync()).ReturnsAsync(new List<Tenant> { tenant });
            mailRepository.Setup(r => r.GetImapConfigurationsAsync(tenant)).ReturnsAsync(new List<MailServerConfiguration> { config });

            using var cts = new CancellationTokenSource();
            syncService.Setup(s => s.SyncInboxAsync(config, tenant.TenantId))
                .Returns(() =>
                {
                    cts.Cancel();
                    return Task.CompletedTask;
                });

            var worker = new TestWorker(mailRepository.Object, syncService.Object, logger.Object, CreateConfig());
            await worker.RunAsync(cts.Token);

            syncService.Verify(s => s.SyncInboxAsync(config, tenant.TenantId), Times.Once);
        }

        [Fact]
        public async Task ExecuteAsync_Should_Log_Error_When_Sync_Fails()
        {
            var mailRepository = new Mock<IMailRepository>();
            var syncService = new Mock<IMailBoxSyncService>();
            var logger = new Mock<ILogger<global::MailBoxSyncService.Worker>>();
            var tenant = CreateTenant();
            var config = new MailServerConfiguration { ItemId = "config-1", IsInbound = true };

            mailRepository.Setup(r => r.GetTenantsAsync()).ReturnsAsync(new List<Tenant> { tenant });
            mailRepository.Setup(r => r.GetImapConfigurationsAsync(tenant)).ReturnsAsync(new List<MailServerConfiguration> { config });

            using var cts = new CancellationTokenSource();
            syncService.Setup(s => s.SyncInboxAsync(config, tenant.TenantId))
                .Callback(() => cts.Cancel())
                .ThrowsAsync(new Exception("boom"));

            var worker = new TestWorker(mailRepository.Object, syncService.Object, logger.Object, CreateConfig());
            await worker.RunAsync(cts.Token);

            logger.Verify(
                x => x.Log(
                    LogLevel.Error,
                    It.IsAny<EventId>(),
                    It.IsAny<It.IsAnyType>(),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
                Times.Once);
        }

        private static Tenant CreateTenant()
        {
            return new Tenant
            {
                TenantId = "tenant-1",
                IsDisabled = false,
                DbConnectionString = "mongodb://localhost/test",
                JwtTokenParameters = new JwtTokenParameters
                {
                    PrivateCertificatePassword = "pass",
                    IssueDate = DateTime.UtcNow
                }
            };
        }

        private static IConfiguration CreateConfig()
        {
            return new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["MailBoxSync:PollIntervalSeconds"] = "1"
                })
                .Build();
        }
    }
}
