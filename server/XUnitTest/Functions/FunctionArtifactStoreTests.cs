using FluentAssertions;
using Functions.DomainService.Storage;
using Blocks.Genesis;
using CloudConfiguration.DomainService.Shared.Services;
using CloudConfiguration.DomainService.Storage.Entities;
using Common.InternalService.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace XUnitTest.Functions
{
    /// <summary>
    /// The artifact holds a tenant's source and its whole dependency tree, so where it lands is a
    /// security property, not a naming convention. These cover the part that decides that — the
    /// path — without needing a storage account.
    /// </summary>
    public class FunctionArtifactStoreTests
    {
        [Fact]
        public void An_artifact_is_addressed_by_tenant_and_build_id()
        {
            FunctionArtifactStore.BlobPath("tenant-1", "abc123").Should().Be("tenant-1/abc123.tar");
        }

        /// <summary>
        /// Build ids are never reused, so a path is never reused for different content — which is
        /// what makes an upload safe while another host may still be reading the previous one.
        /// </summary>
        [Fact]
        public void Different_content_never_shares_a_path()
        {
            FunctionArtifactStore.BlobPath("tenant-1", "aaa")
                .Should().NotBe(FunctionArtifactStore.BlobPath("tenant-1", "bbb"));
        }

        [Fact]
        public void One_tenant_can_never_be_addressed_from_anothers_prefix()
        {
            FunctionArtifactStore.BlobPath("tenant-1", "abc")
                .Should().StartWith("tenant-1/");
            FunctionArtifactStore.BlobPath("tenant-2", "abc")
                .Should().StartWith("tenant-2/");
        }

        /// <summary>
        /// Azure container and blob names are case-sensitive, and a tenant id that reached here in
        /// two casings would be two tenants as far as the store is concerned.
        /// </summary>
        [Fact]
        public void Tenant_casing_does_not_split_one_tenant_into_two()
        {
            FunctionArtifactStore.BlobPath("Tenant-1", "abc")
                .Should().Be(FunctionArtifactStore.BlobPath("tenant-1", "abc"));
        }

        /// <summary>
        /// Both halves reach a URL. A separator in either would let a path climb out of the tenant's
        /// prefix, which is the only thing keeping one tenant's artifact away from another.
        /// </summary>
        [Theory]
        [InlineData("../other-tenant")]
        [InlineData("tenant/../other")]
        [InlineData("tenant\\other")]
        [InlineData("a/b")]
        public void A_tenant_id_that_could_climb_out_is_refused(string tenantId)
        {
            var act = () => FunctionArtifactStore.BlobPath(tenantId, "abc");

            act.Should().Throw<ArgumentException>();
        }

        [Theory]
        [InlineData("../abc")]
        [InlineData("a/b")]
        [InlineData("a\\b")]
        public void An_artifact_id_that_could_climb_out_is_refused(string hash)
        {
            var act = () => FunctionArtifactStore.BlobPath("tenant-1", hash);

            act.Should().Throw<ArgumentException>();
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void A_missing_tenant_is_refused_rather_than_defaulted(string? tenantId)
        {
            // There is no sensible fallback. Writing to a blank prefix would put one tenant's source
            // at the root of a container every other tenant's SAS could be issued against.
            var act = () => FunctionArtifactStore.BlobPath(tenantId!, "abc");

            act.Should().Throw<ArgumentException>();
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        public void A_missing_artifact_id_is_refused(string? hash)
        {
            var act = () => FunctionArtifactStore.BlobPath("tenant-1", hash!);

            act.Should().Throw<ArgumentException>();
        }

        /// <summary>
        /// Kept in their own folder of the tenant's container or bucket, apart from the tenant's
        /// own files, and under the tenant prefix.
        /// </summary>
        [Fact]
        public void An_artifact_lives_in_its_own_folder_of_the_tenants_storage()
        {
            FunctionArtifactStore.ObjectKey("Tenant-1", "b1").Should().Be("blocks-fn-artifacts/tenant-1/b1.tar");
        }

        // ---- tenant storage -------------------------------------------------------------

        private const string Tenant = "tenant-1";

        private readonly Mock<IConfigurationRepository> _configurations = new(MockBehavior.Strict);
        private readonly Mock<IStorageServiceFactory> _factory = new(MockBehavior.Strict);
        private readonly Mock<IStorageService> _storage = new(MockBehavior.Strict);

        private FunctionArtifactStore Store() =>
            new(new ServiceCollection()
                    .AddSingleton(_configurations.Object)
                    .AddSingleton(_factory.Object)
                    .BuildServiceProvider(),
                NullLogger<FunctionArtifactStore>.Instance);

        private void TenantStorageIs(string? strategy)
        {
            _configurations
                .Setup(c => c.GetStorageConfigurationByNameAsync(FunctionArtifactStore.StorageConfigurationName))
                .ReturnsAsync(strategy is null ? null! : new StorageConfiguration { Name = "Default", StorageStrategy = strategy });
            _factory.Setup(f => f.GetStorageService(It.IsAny<StorageConfiguration>())).Returns(_storage.Object);
        }

        /// <summary>
        /// The regression. The store sits under ActionFunctionNode, which the workflow engine takes
        /// with every other node executor — a constructor that touches storage fails every workflow.
        /// Strict mocks with nothing set up: any call at all would throw.
        /// </summary>
        [Fact]
        public void Constructing_the_store_touches_nothing()
        {
            var act = () => Store();

            act.Should().NotThrow();
        }

        /// <summary>
        /// A host that never registered the storage stack (the Worker did not) gets "unavailable" —
        /// and so the image path — rather than a store that cannot be constructed.
        /// </summary>
        [Fact]
        public async Task A_host_without_the_storage_stack_constructs_the_store_and_gets_unavailable()
        {
            var store = new FunctionArtifactStore(
                new ServiceCollection().BuildServiceProvider(), NullLogger<FunctionArtifactStore>.Instance);

            var act = () => store.CreateUploadUrlAsync(Tenant, "b1", TimeSpan.FromMinutes(5));

            await act.Should().ThrowAsync<FunctionArtifactStoreUnavailableException>();
        }

        /// <summary>
        /// Through the real registration, in a host as bare as the Worker's: the store resolves, and
        /// the storage stack it needs is there for it to find.
        /// </summary>
        [Fact]
        public void The_functions_registration_alone_gives_a_resolvable_store_and_the_storage_stack()
        {
            var services = new ServiceCollection().AddLogging();
            global::Functions.DomainService.Utils.ApplicationServiceCollectionExtensions.AddFunctionsServices(services);
            using var provider = services.BuildServiceProvider();

            provider.GetRequiredService<IFunctionArtifactStore>().Should().BeOfType<FunctionArtifactStore>();
            services.Should().Contain(d => d.ServiceType == typeof(IStorageServiceFactory));
            services.Should().Contain(d => d.ServiceType == typeof(IConfigurationRepository));
        }

        [Theory]
        [InlineData("Azure")]
        [InlineData("aws")]
        [InlineData("S3Compatible")]
        public async Task A_signing_provider_signs_the_upload_for_the_artifacts_key(string strategy)
        {
            TenantStorageIs(strategy);
            _storage.Setup(s => s.GeneratePreSignedUploadUrlAsync("blocks-fn-artifacts/tenant-1/b1.tar", TimeSpan.FromMinutes(5)))
                .Returns("https://signed/upload");

            var url = await Store().CreateUploadUrlAsync(Tenant, "b1", TimeSpan.FromMinutes(5));

            url.Should().Be("https://signed/upload");
        }

        /// <summary>Private: the SAS stays on the URL rather than leaning on the container being public.</summary>
        [Fact]
        public async Task A_download_url_is_asked_for_privately_and_for_the_artifacts_key()
        {
            TenantStorageIs("azure");
            DownloadUrlRequest? asked = null;
            _storage.Setup(s => s.GetDownloadUrlAsync(It.IsAny<DownloadUrlRequest>()))
                .Callback((DownloadUrlRequest r) => asked = r)
                .ReturnsAsync("https://signed/download");

            var url = await Store().CreateDownloadUrlAsync(Tenant, "b1", TimeSpan.FromHours(1));

            url.Should().Be("https://signed/download");
            asked!.FileName.Should().Be("blocks-fn-artifacts/tenant-1/b1.tar");
            asked.AccessModifier.Should().Be(AccessModifier.Private);
            asked.ExpiryDuration.Should().Be(TimeSpan.FromHours(1));
        }

        [Fact]
        public async Task A_tenant_without_storage_is_unavailable_not_a_crash()
        {
            TenantStorageIs(null);

            var act = () => Store().CreateUploadUrlAsync(Tenant, "b1", TimeSpan.FromMinutes(5));

            await act.Should().ThrowAsync<FunctionArtifactStoreUnavailableException>();
        }

        /// <summary>
        /// SFTP cannot sign a URL the runner can use — its upload is not implemented and its
        /// download goes through a Blocks controller. Refused before the provider is even opened.
        /// </summary>
        [Fact]
        public async Task An_sftp_tenant_is_unavailable_and_its_storage_is_never_opened()
        {
            TenantStorageIs("SftpStorage");

            var act = () => Store().CreateUploadUrlAsync(Tenant, "b1", TimeSpan.FromMinutes(5));

            await act.Should().ThrowAsync<FunctionArtifactStoreUnavailableException>();
            _factory.Verify(f => f.GetStorageService(It.IsAny<StorageConfiguration>()), Times.Never);
        }

        /// <summary>
        /// Opening the provider can fail (credentials, network, container creation). That becomes
        /// "unavailable", and the provider's own message — which can quote account details — does
        /// not travel with it.
        /// </summary>
        [Fact]
        public async Task A_provider_that_fails_to_open_is_unavailable_without_echoing_its_message()
        {
            TenantStorageIs("azure");
            _factory.Setup(f => f.GetStorageService(It.IsAny<StorageConfiguration>()))
                .Throws(new FormatException("AccountKey=c2VjcmV0 is not valid"));

            var act = () => Store().CreateUploadUrlAsync(Tenant, "b1", TimeSpan.FromMinutes(5));

            var thrown = await act.Should().ThrowAsync<FunctionArtifactStoreUnavailableException>();
            thrown.Which.Message.Should().NotContain("c2VjcmV0");
            thrown.Which.InnerException.Should().BeNull();
        }

        [Fact]
        public async Task A_cancelled_call_is_cancelled_not_unavailable()
        {
            using var cts = new CancellationTokenSource();
            await cts.CancelAsync();

            var act = () => Store().CreateUploadUrlAsync(Tenant, "b1", TimeSpan.FromMinutes(5), cts.Token);

            await act.Should().ThrowAsync<OperationCanceledException>();
        }

        // ---- the tenant context ---------------------------------------------------------

        /// <summary>
        /// The storage stack reads the tenant from the ambient context. In the Worker there is none,
        /// or another tenant's — so the store must stand in the requested tenant for the call, and
        /// put back whatever was there.
        /// </summary>
        [Theory]
        [InlineData(null)]
        [InlineData("other-tenant")]
        public async Task The_call_runs_as_the_requested_tenant_and_the_previous_context_comes_back(string? ambientTenant)
        {
            var previous = BlocksContext.GetContext();
            try
            {
                if (ambientTenant is not null)
                {
                    BlocksContext.SetContext(
                        BlocksContext.Create(ambientTenant, [], "user", true, "", "", DateTime.MaxValue, "", [], "", "", "", "", "", ambientTenant),
                        true);
                }

                string? seen = null;
                _configurations
                    .Setup(c => c.GetStorageConfigurationByNameAsync(FunctionArtifactStore.StorageConfigurationName))
                    .Callback(() => seen = BlocksContext.GetContext()?.TenantId)
                    .ReturnsAsync(new StorageConfiguration { Name = "Default", StorageStrategy = "azure" });
                _factory.Setup(f => f.GetStorageService(It.IsAny<StorageConfiguration>())).Returns(_storage.Object);
                _storage.Setup(s => s.GeneratePreSignedUploadUrlAsync(It.IsAny<string>(), It.IsAny<TimeSpan>())).Returns("u");

                await Store().CreateUploadUrlAsync(Tenant, "b1", TimeSpan.FromMinutes(5));

                seen.Should().Be(Tenant);
                BlocksContext.GetContext()?.TenantId.Should().Be(ambientTenant);
                if (ambientTenant is null)
                {
                    BlocksContext.GetContext().Should().BeNull();
                }
            }
            finally
            {
                BlocksContext.SetContext(previous!, previous is not null);
            }
        }

        [Fact]
        public async Task The_previous_context_comes_back_even_when_the_call_fails()
        {
            var previous = BlocksContext.GetContext();
            try
            {
                BlocksContext.SetContext(
                    BlocksContext.Create("other-tenant", [], "user", true, "", "", DateTime.MaxValue, "", [], "", "", "", "", "", "other-tenant"),
                    true);
                TenantStorageIs(null);

                var act = () => Store().CreateUploadUrlAsync(Tenant, "b1", TimeSpan.FromMinutes(5));
                await act.Should().ThrowAsync<FunctionArtifactStoreUnavailableException>();

                BlocksContext.GetContext()!.TenantId.Should().Be("other-tenant");
            }
            finally
            {
                BlocksContext.SetContext(previous!, previous is not null);
            }
        }
    }
}
