using Blocks.Genesis;
using DomainService.Certificate;
using DomainService.Dtos;
using DomainService.Entities;
using DomainService.Projects;
using DomainService.Shared;
using DomainService.Shared.Entities;
using DomainService.Shared.Services;
using DomainService.Storage;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Moq;
using StorageDriver;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using XUnitTest.TestHelpers;

namespace XUnitTest.Identifier
{
    public class ProjectManagementServiceTests : IDisposable
    {
        private readonly Mock<IProjectRepository> _projectRepository = new();
        private readonly Mock<IBlocksSecret> _blocksSecret = new();
        private readonly Mock<IMessageClient> _messageClient = new();
        private readonly Mock<IConfiguration> _configuration = new();
        private readonly Mock<IStorageDriverService> _storageDriverService = new();
        private readonly Mock<ITenants> _tenants = new();
        private readonly Mock<ICertificateManager> _certificateManager = new();
        private readonly Mock<IEncodingService> _encodingService = new();
        private readonly Mock<ICacheClient> _cacheClient = new();

        public ProjectManagementServiceTests()
        {
            TestBlocksContext.Set();
            _blocksSecret.SetupGet(s => s.DatabaseConnectionString).Returns("mongodb://localhost");
            _configuration.Setup(c => c["KbtclIdentifier"]).Returns(".blocks.test");
            _encodingService.Setup(e => e.EncodeToBase26Async(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>()))
                .ReturnsAsync("abcde");
        }

        public void Dispose() => TestBlocksContext.Clear();

        private ProjectManagementService CreateService() => new(
            _projectRepository.Object,
            _blocksSecret.Object,
            _messageClient.Object,
            _configuration.Object,
            _storageDriverService.Object,
            _tenants.Object,
            _certificateManager.Object,
            _encodingService.Object,
            _cacheClient.Object);

        private static Tenant Project(string tenantId = "DTENANT-1",
                                      CertificateStorageType storageType = CertificateStorageType.Filefilesystem) => new()
        {
            ItemId = "project-item-1",
            TenantId = tenantId,
            TenantGroupId = "group-1",
            Name = "Demo",
            Environment = "dev",
            DbConnectionString = "mongodb://localhost",
            Applications = [new Applications { Domain = "https://demo.test", CookieDomain = "demo.test", IsDomainVerified = true }],
            JwtTokenParameters = new JwtTokenParameters
            {
                PrivateCertificatePassword = "private-pw",
                PublicCertificatePassword = "public-pw",
                IssueDate = DateTime.UtcNow,
                CertificateStorageType = storageType
            }
        };

        private static X509Certificate2 SelfSignedCertificate(string password)
        {
            using var rsa = RSA.Create(2048);
            var request = new CertificateRequest("CN=blocks-test", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
            return X509CertificateLoader.LoadPkcs12(
                certificate.Export(X509ContentType.Pkcs12, password), password, X509KeyStorageFlags.Exportable);
        }

        #region SaveProjectAsync

        [Fact]
        public async Task SaveProjectAsync_NoGroupId_CreatesAssetAndOneTenantPerApplicationContext()
        {
            TenantAsset? savedAsset = null;
            _projectRepository.Setup(r => r.UpdateTenantAssetAsync(It.IsAny<TenantAsset>()))
                .Callback<TenantAsset>(a => savedAsset = a)
                .Returns(Task.CompletedTask);

            var request = new CreateProjectRequest
            {
                Name = "My Project",
                IsAcceptBlocksTerms = true,
                IsUseBlocksExclusively = true,
                Resources = [new Resource { ResourceId = "repo-1", Name = "web", Link = "https://git.test/web" }],
                applicationContexts =
                [
                    new ApplicationContext { Environment = "dev", Domain = "https://dev.test", CookieDomain = "test" },
                    new ApplicationContext { Environment = "prod", Domain = "https://prod.test", CookieDomain = "test" }
                ]
            };

            var result = await CreateService().SaveProjectAsync(request);

            result.IsSuccess.Should().BeTrue();
            result.TenantGroupId.Should().NotBeNullOrWhiteSpace();
            savedAsset.Should().NotBeNull();
            savedAsset!.Resources.Should().HaveCount(1);
            _projectRepository.Verify(r => r.InsertProjectAsync(It.IsAny<Tenant>()), Times.Exactly(2));
            _projectRepository.Verify(r => r.SaveRepoInfoAsync(It.IsAny<Tenant>(), It.IsAny<List<Resource>>()), Times.Exactly(2));
            _messageClient.Verify(m => m.SendToConsumerAsync(It.IsAny<ConsumerMessage<Tenant>>()), Times.Exactly(2));
        }

        [Fact]
        public async Task SaveProjectAsync_ExistingGroupId_ReusesStoredAssets()
        {
            _projectRepository.Setup(r => r.GetTenantAssetAsync(It.IsAny<GetAssetRequest>()))
                .ReturnsAsync((new TenantAsset
                {
                    TenantGroupId = "group-1",
                    Resources = [new Resource { ResourceId = "repo-9", Name = "api", Link = "https://git.test/api" }]
                }, 1));

            var request = new CreateProjectRequest
            {
                Name = "My Project",
                TenantGroupId = "group-1",
                applicationContexts = [new ApplicationContext { Environment = "stg", Domain = "https://stg.test", CookieDomain = "test" }]
            };

            var result = await CreateService().SaveProjectAsync(request);

            result.TenantGroupId.Should().Be("group-1");
            request.Resources.Should().ContainSingle(r => r.ResourceId == "repo-9");
            _projectRepository.Verify(r => r.UpdateTenantAssetAsync(It.IsAny<TenantAsset>()), Times.Never);
            _projectRepository.Verify(r => r.InsertProjectAsync(
                It.Is<Tenant>(t => t.TenantGroupId == "group-1" && t.Environment == "stg")), Times.Once);
        }

        [Fact]
        public async Task SaveProjectAsync_WithoutResources_StillBuildsDefaultDomain()
        {
            var request = new CreateProjectRequest
            {
                Name = "My Project",
                applicationContexts = [new ApplicationContext { Environment = "dev", Domain = "https://dev.test", CookieDomain = "test" }]
            };

            Tenant? inserted = null;
            _projectRepository.Setup(r => r.InsertProjectAsync(It.IsAny<Tenant>()))
                .Callback<Tenant>(t => inserted = t)
                .Returns(Task.CompletedTask);

            await CreateService().SaveProjectAsync(request);

            inserted.Should().NotBeNull();
            inserted!.JwtTokenParameters.Audiences.Should().ContainSingle();
            inserted.JwtTokenParameters.Issuer.Should().Be(IdentifierConstants.Issuer);
            inserted.TenantId.Should().StartWith("D");
        }

        #endregion

        #region ConfigureProjectAsync and certificate upload

        [Fact]
        public async Task ConfigureProjectAsync_HappyPath_UploadsCertificatesAndTracksProgress()
        {
            var project = Project();
            var publicCert = SelfSignedCertificate("public-pw");
            var privateCert = SelfSignedCertificate("private-pw");
            _certificateManager.Setup(c => c.GenerateCertificates(It.IsAny<JwtTokenParameters>()))
                .Returns((publicCert, privateCert));
            _certificateManager.Setup(c => c.GeneratePrivateCertificateName(It.IsAny<string>(), It.IsAny<string>()))
                .Returns("private-cert-name");
            _storageDriverService.Setup(s => s.UploadFileToLocalStorageAsync(It.IsAny<LocalStorageUploadRequest>()))
                .ReturnsAsync(new LocalStorageUploadResponse { IsSuccess = true });
            _storageDriverService.Setup(s => s.GetUrlForDownloadFileAsync(It.IsAny<GetFileRequest>()))
                .ReturnsAsync(new FileResponse { Url = "https://files.test/public.pfx" });

            var tracer = new ProjectStatusTracer { ProjectId = project.ItemId };

            await CreateService().ConfigureProjectAsync(project, tracer);

            tracer.ErrorMessage.Should().BeNullOrEmpty();
            tracer.IsCertificatesUploaded.Should().BeTrue();
            tracer.IsProjectUpdated.Should().BeTrue();
            tracer.InsertedIntoProjectPeople.Should().BeTrue();
            project.JwtTokenParameters.PublicCertificatePath.Should().Be("https://files.test/public.pfx");
            _certificateManager.Verify(c => c.UploadPrivateCertificateAsync(
                CertificateStorageType.Filefilesystem, privateCert, "private-pw", "private-cert-name"), Times.Once);
            _projectRepository.Verify(r => r.InsertPeopleAsync(It.Is<ProjectPeople>(p => p.IsCreator && p.TenantId == project.TenantId)), Times.Once);
        }

        [Fact]
        public async Task ConfigureProjectAsync_AlreadyCompletedSteps_SkipsThem()
        {
            var project = Project();
            var publicCert = SelfSignedCertificate("public-pw");
            var privateCert = SelfSignedCertificate("private-pw");
            _certificateManager.Setup(c => c.GenerateCertificates(It.IsAny<JwtTokenParameters>()))
                .Returns((publicCert, privateCert));

            var tracer = new ProjectStatusTracer
            {
                ProjectId = project.ItemId,
                IsCertificatesUploaded = true,
                IsProjectUpdated = true,
                InsertedIntoProjectPeople = true
            };

            await CreateService().ConfigureProjectAsync(project, tracer);

            _certificateManager.Verify(c => c.UploadPrivateCertificateAsync(
                It.IsAny<CertificateStorageType>(), It.IsAny<X509Certificate2>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
            _projectRepository.Verify(r => r.UpdateProjectAsync(It.IsAny<Tenant>()), Times.Never);
            _projectRepository.Verify(r => r.InsertPeopleAsync(It.IsAny<ProjectPeople>()), Times.Never);
            _projectRepository.Verify(r => r.CreateDefaultConfigurationAsync(tracer, project), Times.Once);
        }

        [Fact]
        public async Task ConfigureProjectAsync_CertificateGenerationFails_RecordsErrorOnTracer()
        {
            var project = Project();
            _certificateManager.Setup(c => c.GenerateCertificates(It.IsAny<JwtTokenParameters>()))
                .Throws(new InvalidOperationException("no entropy"));

            await CreateService().ConfigureProjectAsync(project);

            _projectRepository.Verify(r => r.SaveStatusTracerAsync(
                It.Is<ProjectStatusTracer>(t => t.ErrorMessage == "no entropy")), Times.Once);
        }

        [Fact]
        public async Task UploadPublicCertificateAsync_UnsupportedStorageType_Throws()
        {
            var project = Project();
            project.JwtTokenParameters.CertificateStorageType = (CertificateStorageType)99;

            var act = async () => await CreateService().UploadPublicCertificateAsync(SelfSignedCertificate("public-pw"), project);

            await act.Should().ThrowAsync<NotSupportedException>();
        }

        [Fact]
        public async Task UploadPublicCertificateIntoFileSystemAsync_UploadFails_ReturnsEmptyUrl()
        {
            _storageDriverService.Setup(s => s.UploadFileToLocalStorageAsync(It.IsAny<LocalStorageUploadRequest>()))
                .ReturnsAsync(new LocalStorageUploadResponse { IsSuccess = false });

            var url = await CreateService().UploadPublicCertificateIntoFileSystemAsync(SelfSignedCertificate("public-pw"), Project());

            url.Should().BeEmpty();
            _storageDriverService.Verify(s => s.GetUrlForDownloadFileAsync(It.IsAny<GetFileRequest>()), Times.Never);
        }

        #endregion

        #region GetAllAsync / RestoreProjectAsync / RestoreUnfinishedProjectAsync

        [Fact]
        public async Task GetAllAsync_DelegatesToRepository()
        {
            var expected = new List<GroupedProjectsDto> { new() { TenantGroupId = "group-1" } };
            _projectRepository.Setup(r => r.GetAllByLastModifiedDateAsync(It.IsAny<GetProjectsRequest>())).ReturnsAsync(expected);

            var result = await CreateService().GetAllAsync(new GetProjectsRequest());

            result.Should().BeSameAs(expected);
        }

        [Fact]
        public async Task RestoreProjectAsync_QueuesRestoreRequest()
        {
            var result = await CreateService().RestoreProjectAsync(new RestoreProjectRequest { ProjectId = "project-item-1" });

            result.IsSuccess.Should().BeTrue();
            _messageClient.Verify(m => m.SendToConsumerAsync(
                It.Is<ConsumerMessage<RestoreProjectRequest>>(c => c.Payload.ProjectId == "project-item-1")), Times.Once);
        }

        [Fact]
        public async Task RestoreUnfinishedProjectAsync_SuccessfulConfiguration_MarksTracerComplete()
        {
            var project = Project();
            var tracer = new ProjectStatusTracer { ProjectId = project.ItemId, ErrorMessage = "previous failure" };
            _projectRepository.Setup(r => r.GetAllUnfinishedProjectAsync()).ReturnsAsync([tracer]);
            _projectRepository.Setup(r => r.GetByIdAsync(project.ItemId)).ReturnsAsync(project);
            _certificateManager.Setup(c => c.GenerateCertificates(It.IsAny<JwtTokenParameters>()))
                .Returns((SelfSignedCertificate("public-pw"), SelfSignedCertificate("private-pw")));
            _storageDriverService.Setup(s => s.UploadFileToLocalStorageAsync(It.IsAny<LocalStorageUploadRequest>()))
                .ReturnsAsync(new LocalStorageUploadResponse { IsSuccess = true });
            _storageDriverService.Setup(s => s.GetUrlForDownloadFileAsync(It.IsAny<GetFileRequest>()))
                .ReturnsAsync(new FileResponse { Url = "https://files.test/public.pfx" });

            await CreateService().RestoreUnfinishedProjectAsync();

            tracer.IsProjectCreationSuccess.Should().BeTrue();
            _projectRepository.Verify(r => r.SaveStatusTracerAsync(tracer), Times.Once);
        }

        [Fact]
        public async Task RestoreUnfinishedProjectAsync_ConfigurationFails_LeavesTracerIncomplete()
        {
            var project = Project();
            var tracer = new ProjectStatusTracer { ProjectId = project.ItemId };
            _projectRepository.Setup(r => r.GetAllUnfinishedProjectAsync()).ReturnsAsync([tracer]);
            _projectRepository.Setup(r => r.GetByIdAsync(project.ItemId)).ReturnsAsync(project);
            _certificateManager.Setup(c => c.GenerateCertificates(It.IsAny<JwtTokenParameters>()))
                .Throws(new InvalidOperationException("no entropy"));

            await CreateService().RestoreUnfinishedProjectAsync();

            tracer.IsProjectCreationSuccess.Should().BeFalse();
            tracer.ErrorMessage.Should().Be("no entropy");
        }

        #endregion

        #region GetAsync

        [Fact]
        public async Task GetAsync_ProjectNotFound_ReturnsProjectNotExistError()
        {
            _projectRepository.Setup(r => r.GetByTenantIdAsync("tenant-123")).ReturnsAsync((Tenant)null!);

            var result = await CreateService().GetAsync();

            result.Data.Should().BeNull();
            result.Errors.Should().ContainKey("project_not_exist");
        }

        [Fact]
        public async Task GetAsync_WithBlocksGuid_BuildsTenantSlug()
        {
            var project = Project();
            _projectRepository.Setup(r => r.GetByTenantIdAsync("tenant-123")).ReturnsAsync(project);
            _projectRepository.Setup(r => r.GetBlocksGuidAsync("group-1"))
                .ReturnsAsync(new BlocksGuid { EncodedValue = "xyzab" });

            var result = await CreateService().GetAsync();

            result.Errors.Should().BeNull();
            result.Data!.TenantSlug.Should().Be("dxyzab");
            result.Data.Name.Should().Be("Demo");
            result.Data.IsDomainVerified.Should().BeTrue();
        }

        [Fact]
        public async Task GetAsync_WithoutBlocksGuid_LeavesTenantSlugEmpty()
        {
            _projectRepository.Setup(r => r.GetByTenantIdAsync("tenant-123")).ReturnsAsync(Project());
            _projectRepository.Setup(r => r.GetBlocksGuidAsync("group-1")).ReturnsAsync((BlocksGuid)null!);

            var result = await CreateService().GetAsync();

            result.Data!.TenantSlug.Should().BeEmpty();
        }

        #endregion

        #region UpdateProjectAsync / DisableProjectAsync

        [Fact]
        public async Task UpdateProjectAsync_ProjectNotFound_ReturnsProjectNotFound()
        {
            _projectRepository.Setup(r => r.GetByTenantIdAsync("tenant-123")).ReturnsAsync((Tenant)null!);

            var result = await CreateService().UpdateProjectAsync(new UpdateProjectRequest { ApplicationDomain = "https://new.test" });

            result.IsSuccess.Should().BeFalse();
            result.Errors.Should().ContainKey("project_not_found");
        }

        [Fact]
        public async Task UpdateProjectAsync_ExistingProject_UpdatesAudiencesAndCache()
        {
            var project = Project();
            _projectRepository.Setup(r => r.GetByTenantIdAsync("tenant-123")).ReturnsAsync(project);

            var result = await CreateService().UpdateProjectAsync(new UpdateProjectRequest { ApplicationDomain = "https://new.test" });

            result.IsSuccess.Should().BeTrue();
            project.JwtTokenParameters.Audiences.Should().BeEquivalentTo(["https://new.test"]);
            project.LastUpdatedBy.Should().Be("user-123");
            _projectRepository.Verify(r => r.UpdateProjectAsync(project), Times.Once);
            _projectRepository.Verify(r => r.UpdateIamConfiguration(project), Times.Once);
            _tenants.Verify(t => t.UpdateTenantVersionAsync(
                It.Is<TenantCacheUpdateMessage>(m => m.Action == "upsert" && m.TenantId == project.TenantId)), Times.Once);
        }

        [Fact]
        public async Task DisableProjectAsync_UnknownProject_ReturnsProjectNotFound()
        {
            _tenants.Setup(t => t.GetTenantByID("DTENANT-1")).Returns((Tenant)null!);

            var result = await CreateService().DisableProjectAsync("DTENANT-1");

            result.IsSuccess.Should().BeFalse();
            result.Errors.Should().ContainKey("project_not_found");
        }

        [Fact]
        public async Task DisableProjectAsync_KnownProject_DisablesAndPublishesDomainBinding()
        {
            var project = Project();
            _tenants.Setup(t => t.GetTenantByID("DTENANT-1")).Returns(project);

            var result = await CreateService().DisableProjectAsync("DTENANT-1");

            result.IsSuccess.Should().BeTrue();
            project.IsDisabled.Should().BeTrue();
            _projectRepository.Verify(r => r.UpdateProjectAsync(project), Times.Once);
            _messageClient.Verify(m => m.SendToConsumerAsync(It.Is<ConsumerMessage<DisableDomainBindingRequest>>(
                c => c.Payload.Domain == IdentifierConstants.CookieDomainPrefix + "demo.test"
                     && c.Payload.ProjectId == project.ItemId)), Times.Once);
        }

        #endregion

        #region Assets

        [Fact]
        public async Task GetAssetAsync_ReturnsRepositoryResult()
        {
            var asset = new TenantAsset { TenantGroupId = "group-1", Resources = [] };
            _projectRepository.Setup(r => r.GetTenantAssetAsync(It.IsAny<GetAssetRequest>())).ReturnsAsync((asset, 7));

            var result = await CreateService().GetAssetAsync(new GetAssetRequest { TenantGroupId = "group-1" });

            result.IsSuccess.Should().BeTrue();
            result.Assets.Should().BeSameAs(asset);
            result.TotalCount.Should().Be(7);
        }

        [Fact]
        public async Task AddAssetAsync_NoExistingAsset_CreatesAssetAndSaves()
        {
            _projectRepository.Setup(r => r.GetTenantAssetAsync(It.IsAny<GetAssetRequest>()))
                .ReturnsAsync(((TenantAsset)null!, 0));

            var request = new AddAssetRequest
            {
                TenantGroupId = "group-1",
                Resource = new Resource { ResourceId = "repo-1", Name = "web", Link = "https://git.test/web" }
            };

            var result = await CreateService().AddAssetAsync(request);

            result.IsSuccess.Should().BeTrue();
            _projectRepository.Verify(r => r.SaveTenantAssetAsync(
                It.Is<TenantAsset>(a => a.TenantGroupId == "group-1" && a.Resources.Count == 1)), Times.Once);
            _projectRepository.Verify(r => r.UpdateRepoResourceAsync(request), Times.Once);
        }

        [Fact]
        public async Task AddAssetAsync_ResourceAlreadyPresent_DoesNotSaveAgain()
        {
            var existing = new TenantAsset
            {
                TenantGroupId = "group-1",
                Resources = [new Resource { ResourceId = "repo-1", Name = "web", Link = "https://git.test/web" }]
            };
            _projectRepository.Setup(r => r.GetTenantAssetAsync(It.IsAny<GetAssetRequest>())).ReturnsAsync((existing, 1));

            var result = await CreateService().AddAssetAsync(new AddAssetRequest
            {
                TenantGroupId = "group-1",
                Resource = new Resource { ResourceId = "repo-1", Name = "web", Link = "https://git.test/web" }
            });

            result.IsSuccess.Should().BeTrue();
            existing.Resources.Should().HaveCount(1);
            _projectRepository.Verify(r => r.SaveTenantAssetAsync(It.IsAny<TenantAsset>()), Times.Never);
            _projectRepository.Verify(r => r.UpdateRepoResourceAsync(It.IsAny<AddAssetRequest>()), Times.Never);
        }

        #endregion

        #region Third party token parameters and claims

        [Fact]
        public async Task UpdateTokenValidationParametersAsync_ProjectNotFound_ReturnsProjectNotFound()
        {
            _projectRepository.Setup(r => r.GetByTenantIdAsync("tenant-123")).ReturnsAsync((Tenant)null!);

            var result = await CreateService().UpdateTokenValidationParametersAsync(new UpdateTokenValidationParametersRequest());

            result.IsSuccess.Should().BeFalse();
            result.Errors.Should().ContainKey("project_not_found");
        }

        [Fact]
        public async Task UpdateTokenValidationParametersAsync_ExistingProject_StoresParametersAndClearsCache()
        {
            var project = Project();
            _projectRepository.Setup(r => r.GetByTenantIdAsync("tenant-123")).ReturnsAsync(project);

            var request = new UpdateTokenValidationParametersRequest
            {
                ProviderName = "auth0",
                Issuer = "https://issuer.test",
                Audiences = ["aud-1"],
                PublicCertificatePath = "https://files.test/cert.pfx",
                PublicCertificatePassword = "pw",
                JwksUrl = "https://issuer.test/jwks"
            };

            var result = await CreateService().UpdateTokenValidationParametersAsync(request);

            result.IsSuccess.Should().BeTrue();
            project.ThirdPartyJwtTokenParameters.ProviderName.Should().Be("auth0");
            project.ThirdPartyJwtTokenParameters.JwksUrl.Should().Be("https://issuer.test/jwks");
            _projectRepository.Verify(r => r.UpdateProjectAsync(project), Times.Once);
            _cacheClient.Verify(c => c.RemoveKeyAsync("tetocertpublic::tenant-123"), Times.Once);
        }

        [Fact]
        public async Task GetProjectTokenValidationParametersAsync_UnknownProject_ReturnsNotFound()
        {
            _projectRepository.Setup(r => r.GetByTenantIdAsync("missing")).ReturnsAsync((Tenant)null!);

            var result = await CreateService().GetProjectTokenValidationParametersAsync("missing");

            result.Should().BeOfType<NotFoundObjectResult>();
        }

        [Fact]
        public async Task GetProjectTokenValidationParametersAsync_KnownProject_ReturnsOk()
        {
            var project = Project();
            project.ThirdPartyJwtTokenParameters.ProviderName = "auth0";
            _projectRepository.Setup(r => r.GetByTenantIdAsync("DTENANT-1")).ReturnsAsync(project);

            var result = await CreateService().GetProjectTokenValidationParametersAsync("DTENANT-1");

            result.Should().BeOfType<OkObjectResult>();
            result.As<OkObjectResult>().Value.Should().NotBeNull();
        }

        [Fact]
        public async Task SaveThirdPartyJWTClaimsAsync_NewMapper_GeneratesItemId()
        {
            ThirdPartyJWTClaims? saved = null;
            _projectRepository.Setup(r => r.SaveJWTClaimsAsync(It.IsAny<ThirdPartyJWTClaims>()))
                .Callback<ThirdPartyJWTClaims>(c => saved = c)
                .ReturnsAsync(new BaseResponse { IsSuccess = true });

            var result = await CreateService().SaveThirdPartyJWTClaimsAsync(new SaveThirdPartyJWTClaimsRequest
            {
                UserId = "sub",
                Email = "email",
                Name = "name",
                UserName = "preferred_username",
                Roles = "roles"
            });

            result.IsSuccess.Should().BeTrue();
            result.ItemId.Should().NotBeNullOrWhiteSpace();
            saved!.CreatedBy.Should().Be("user-123");
            saved.UserId.Should().Be("sub");
            _projectRepository.Verify(r => r.GetThirdPartyJWTClaimsAsync(It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task SaveThirdPartyJWTClaimsAsync_ExistingItemId_UpdatesStoredMapper()
        {
            var existing = new ThirdPartyJWTClaims { ItemId = "claims-1", CreatedBy = "someone-else" };
            _projectRepository.Setup(r => r.GetThirdPartyJWTClaimsAsync("claims-1")).ReturnsAsync(existing);
            _projectRepository.Setup(r => r.SaveJWTClaimsAsync(It.IsAny<ThirdPartyJWTClaims>()))
                .ReturnsAsync(new BaseResponse { IsSuccess = true });

            var result = await CreateService().SaveThirdPartyJWTClaimsAsync(new SaveThirdPartyJWTClaimsRequest
            {
                ItemId = "claims-1",
                UserId = "sub"
            });

            result.ItemId.Should().Be("claims-1");
            existing.UserId.Should().Be("sub");
            existing.LastUpdatedBy.Should().Be("user-123");
            existing.CreatedBy.Should().Be("someone-else");
        }

        [Fact]
        public async Task GetThirdPartyJWTClaimsAsync_DelegatesToRepository()
        {
            var claims = new ThirdPartyJWTClaims { ItemId = "claims-1" };
            _projectRepository.Setup(r => r.GetThirdPartyJWTClaimsAsync("claims-1")).ReturnsAsync(claims);

            var result = await CreateService().GetThirdPartyJWTClaimsAsync(new GetThirdPartyJWTClaimsRequest { ItemId = "claims-1" });

            result.Should().BeSameAs(claims);
        }

        [Fact]
        public async Task UpdateTenantGroupAsync_DelegatesToRepository()
        {
            var request = new UpdateTenantGroupRequest { TenantGroupId = "group-1", Name = "Renamed" };

            var result = await CreateService().UpdateTenantGroupAsync(request);

            result.IsSuccess.Should().BeTrue();
            _projectRepository.Verify(r => r.UpdateTenantGroupAsync(request), Times.Once);
        }

        #endregion
    }
}
