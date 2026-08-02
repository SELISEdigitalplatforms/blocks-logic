using Blocks.Genesis;
using DomainService.Certificate;
using DomainService.Projects;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using XUnitTest.TestHelpers;

namespace XUnitTest.Identifier
{
    public class CertificateManagerTests
    {
        private readonly Mock<ICertificateStorageFactory> _storageFactory = new();
        private readonly Mock<ICertificateStorage> _storage = new();
        private readonly Mock<ICryptoService> _cryptoService = new();

        public CertificateManagerTests()
        {
            _storageFactory.Setup(f => f.Create(It.IsAny<CertificateStorageType>())).Returns(_storage.Object);
        }

        private CertificateManager CreateManager() => new(_storageFactory.Object, _cryptoService.Object);

        private static JwtTokenParameters Parameters() => new()
        {
            Issuer = "SeliseBlocks",
            Subject = "Selise-Blocks",
            CertificateValidForNumberOfDays = 30,
            IssueDate = DateTime.UtcNow,
            PrivateCertificatePassword = "private-pw",
            PublicCertificatePassword = string.Empty
        };

        [Fact]
        public void GenerateCertificates_ReturnsAMatchingPublicAndPrivatePair()
        {
            var parameters = Parameters();

            var (publicCertificate, privateCertificate) = CreateManager().GenerateCertificates(parameters);

            publicCertificate.Should().NotBeNull();
            privateCertificate.Should().NotBeNull();
            publicCertificate.Subject.Should().Contain("Selise-Blocks");
            publicCertificate.Issuer.Should().Contain("SeliseBlocks");
            publicCertificate.HasPrivateKey.Should().BeFalse();
            privateCertificate.HasPrivateKey.Should().BeTrue();
            privateCertificate.Thumbprint.Should().Be(publicCertificate.Thumbprint);
            publicCertificate.NotAfter.Date.Should().Be(publicCertificate.NotBefore.Date.AddDays(30));
        }

        [Fact]
        public async Task UploadPrivateCertificateAsync_ResolvesStorageFromTheFactory()
        {
            var (_, privateCertificate) = CreateManager().GenerateCertificates(Parameters());

            await CreateManager().UploadPrivateCertificateAsync(
                CertificateStorageType.Mongodb, privateCertificate, "private-pw", "cert-name");

            _storageFactory.Verify(f => f.Create(CertificateStorageType.Mongodb), Times.Once);
            _storage.Verify(s => s.UploadCertificateAsync(privateCertificate, "private-pw", "cert-name"), Times.Once);
        }

        [Fact]
        public void GeneratePrivateCertificateName_HashesTenantAndItemId()
        {
            byte[]? hashed = null;
            _cryptoService.Setup(c => c.Hash(It.IsAny<byte[]>(), It.IsAny<bool>()))
                .Callback<byte[], bool>((value, _) => hashed = value)
                .Returns("hashed-name");

            var name = CreateManager().GeneratePrivateCertificateName("TENANT-1", "item-1");

            name.Should().Be("hashed-name");
            System.Text.Encoding.UTF8.GetString(hashed!).Should().Be("TENANT-1::item-1");
        }
    }

    public class CertificateStorageFactoryTests
    {
        private readonly Mock<ILogger<CertificateStorageFactory>> _logger = new();
        private readonly Mock<IProjectRepository> _projectRepository = new();

        private CertificateStorageFactory CreateFactory() => new(_logger.Object, _projectRepository.Object);

        [Fact]
        public void Create_FileSystemStorageType_ReturnsLocalSystemStorage()
        {
            CreateFactory().Create(CertificateStorageType.Filefilesystem).Should().BeOfType<LocalSystemStorage>();
        }

        [Fact]
        public void Create_MongodbStorageType_ReturnsMongoDbStorage()
        {
            CreateFactory().Create(CertificateStorageType.Mongodb).Should().BeOfType<MongoDBStorage>();
        }

        [Fact]
        public void Create_UnknownStorageType_Throws()
        {
            Action act = () => CreateFactory().Create((CertificateStorageType)99);

            act.Should().Throw<ArgumentException>().WithParameterName("storageType");
        }

        [Fact]
        public void Create_AzureStorageType_ReturnsAzureKeyVaultStorageWhenConfigured()
        {
            var previous = new Dictionary<string, string?>
            {
                { "KeyVault__KeyVaultUrl", Environment.GetEnvironmentVariable("KeyVault__KeyVaultUrl") },
                { "KeyVault__TenantId", Environment.GetEnvironmentVariable("KeyVault__TenantId") },
                { "KeyVault__ClientId", Environment.GetEnvironmentVariable("KeyVault__ClientId") },
                { "KeyVault__ClientSecret", Environment.GetEnvironmentVariable("KeyVault__ClientSecret") }
            };

            try
            {
                Environment.SetEnvironmentVariable("KeyVault__KeyVaultUrl", "https://blocks-test.vault.azure.net/");
                Environment.SetEnvironmentVariable("KeyVault__TenantId", "00000000-0000-0000-0000-000000000001");
                Environment.SetEnvironmentVariable("KeyVault__ClientId", "00000000-0000-0000-0000-000000000002");
                Environment.SetEnvironmentVariable("KeyVault__ClientSecret", "not-a-real-secret");

                CreateFactory().Create(CertificateStorageType.Azure).Should().BeOfType<AzureKeyVaultStorage>();
            }
            finally
            {
                foreach (var (key, value) in previous)
                {
                    Environment.SetEnvironmentVariable(key, value);
                }
            }
        }
    }

    public class CertificateStorageTests : IDisposable
    {
        private readonly Mock<ILogger> _logger = new();
        private readonly Mock<IProjectRepository> _projectRepository = new();

        public CertificateStorageTests() => TestBlocksContext.Set();

        public void Dispose() => TestBlocksContext.Clear();

        private static X509Certificate2 Certificate(string password)
        {
            using var rsa = RSA.Create(2048);
            var request = new CertificateRequest("CN=blocks-test", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
            return X509CertificateLoader.LoadPkcs12(
                certificate.Export(X509ContentType.Pkcs12, password), password, X509KeyStorageFlags.Exportable);
        }

        // TenantCertificate is declared under the same namespace in two referenced assemblies, so the
        // saved document is inspected dynamically rather than named directly.
        private dynamic SavedCertificate()
        {
            var invocation = _projectRepository.Invocations
                .Single(i => i.Method.Name == nameof(IProjectRepository.SaveTenantCertificate));
            return invocation.Arguments[0];
        }

        [Fact]
        public async Task LocalSystemStorage_UploadCertificateAsync_SavesBase64CertificateAgainstTheTenant()
        {
            await new LocalSystemStorage(_logger.Object, _projectRepository.Object)
                .UploadCertificateAsync(Certificate("pw"), "pw", "cert-name");

            var saved = SavedCertificate();
            ((string)saved.Key).Should().Be("cert-name");
            ((string)saved.CreatedBy).Should().Be("user-123");
            ((string)saved.LastUpdatedBy).Should().Be("user-123");
            Convert.FromBase64String((string)saved.Value).Should().NotBeEmpty();
        }

        [Fact]
        public async Task MongoDbStorage_UploadCertificateAsync_SavesBase64CertificateAgainstTheTenant()
        {
            await new MongoDBStorage(_logger.Object, _projectRepository.Object)
                .UploadCertificateAsync(Certificate("pw"), "pw", "mongo-cert");

            var saved = SavedCertificate();
            ((string)saved.Key).Should().Be("mongo-cert");
            ((string)saved.ItemId).Should().NotBeNullOrWhiteSpace();
            Convert.FromBase64String((string)saved.Value).Should().NotBeEmpty();
        }
    }
}
