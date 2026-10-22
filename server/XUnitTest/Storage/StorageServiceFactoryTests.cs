using Blocks.Genesis.Entities.Data;
using Common.InternalService.Storage;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace XUnitTest.Storage
{
    /// <summary>
    /// Unit tests for <see cref="StorageServiceFactory"/>. The configuration used to pass through
    /// static fields that concurrent requests shared; each service now gets its own through the
    /// constructor. SFTP is the provider whose constructor makes no network call, so it can be built here.
    /// </summary>
    public class StorageServiceFactoryTests
    {
        [Fact]
        public void GetStorageService_InvalidProvider_Throws()
        {
            var factory = new StorageServiceFactory(Mock.Of<IServiceProvider>());

            var act = () => factory.GetStorageService(new StorageConfiguration { StorageStrategy = "unknown-provider" });

            act.Should().Throw<ArgumentException>();
        }

        [Fact]
        public async Task GetStorageService_ParallelTenants_EachServiceGetsOnlyItsOwnConfiguration()
        {
            var services = new ServiceCollection();
            services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
            var factory = new StorageServiceFactory(services.BuildServiceProvider());

            static StorageConfiguration ConfigFor(string tenant) => new()
            {
                StorageStrategy = "SftpStorage",
                Host = $"{tenant}.host",
                Port = "22",
                UserName = $"{tenant}-user",
                Password = $"{tenant}-password",
                RemoteBasePath = $"/{tenant}/",
                SftpSecretKey = $"{tenant}-key"
            };

            static string Field(object service, string name) =>
                (string)service.GetType().GetField(name, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(service)!;

            var jobs = Enumerable.Range(0, 200).Select(i => i % 2 == 0 ? "tenant-a" : "tenant-b")
                .Select(tenant => Task.Run(() => (tenant, service: factory.GetStorageService(ConfigFor(tenant)))));
            var results = await Task.WhenAll(jobs);

            foreach (var (tenant, service) in results)
            {
                Field(service, "_sftpHost").Should().Be($"{tenant}.host");
                Field(service, "_sftpUsername").Should().Be($"{tenant}-user");
                Field(service, "_sftpPassword").Should().Be($"{tenant}-password");
                Field(service, "_remoteBasePath").Should().Be($"/{tenant}");
                Field(service, "_sftpSecretKey").Should().Be($"{tenant}-key");
            }
        }
    }
}
