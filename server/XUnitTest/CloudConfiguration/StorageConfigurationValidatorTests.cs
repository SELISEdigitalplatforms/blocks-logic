using CloudConfiguration.DomainService.Shared.Services;
using CloudConfiguration.DomainService.Storage.Entities;
using CloudConfiguration.DomainService.Storage.RequestModel;
using CloudConfiguration.DomainService.Storage.Validators;
using FluentValidation.TestHelper;
using Moq;

namespace XUnitTest.CloudConfiguration
{
    public class StorageConfigurationValidatorTests
    {
        private readonly Mock<IConfigurationRepository> _repo = new();
        private readonly StorageConfigurationValidator _validator;

        public StorageConfigurationValidatorTests()
        {
            _repo.Setup(r => r.GetStorageConfigurationByNameAsync(It.IsAny<string>()))
                .ReturnsAsync((StorageConfiguration)null!);
            _repo.Setup(r => r.GetStorageConfigurationByIdAsync(It.IsAny<string>()))
                .ReturnsAsync(new StorageConfiguration { Name = "old-name" });
            _validator = new StorageConfigurationValidator(_repo.Object);
        }

        private static SaveStorageConfigurationRequest ValidAzure() => new()
        {
            Name = "cfg",
            StorageStrategy = "Azure",
            ConnectionString = "DefaultEndpointsProtocol=https;AccountName=acct;AccountKey=abc123==;EndpointSuffix=core.windows.net"
        };

        [Fact]
        public async Task EmptyName_Fails()
        {
            var req = ValidAzure();
            req.Name = "";
            var result = await _validator.TestValidateAsync(req);
            result.ShouldHaveValidationErrorFor(x => x.Name);
        }

        [Fact]
        public async Task ValidAzure_Passes()
        {
            var result = await _validator.TestValidateAsync(ValidAzure());
            result.ShouldNotHaveValidationErrorFor(x => x.ConnectionString);
            result.ShouldNotHaveValidationErrorFor(x => x.StorageStrategy);
        }

        [Fact]
        public async Task InvalidAzureConnectionString_Fails()
        {
            var req = ValidAzure();
            req.ConnectionString = "not-a-valid-connection-string";
            var result = await _validator.TestValidateAsync(req);
            result.ShouldHaveValidationErrorFor(x => x.ConnectionString);
        }

        [Fact]
        public async Task UnknownStrategy_Fails()
        {
            var req = ValidAzure();
            req.StorageStrategy = "Dropbox";
            var result = await _validator.TestValidateAsync(req);
            result.ShouldHaveValidationErrorFor(x => x.StorageStrategy);
        }

        [Fact]
        public async Task EmptyStrategy_Fails()
        {
            var req = ValidAzure();
            req.StorageStrategy = "";
            var result = await _validator.TestValidateAsync(req);
            result.ShouldHaveValidationErrorFor(x => x.StorageStrategy);
        }

        [Fact]
        public async Task Aws_MissingKeys_Fails()
        {
            var req = new SaveStorageConfigurationRequest { Name = "cfg", StorageStrategy = "AWS" };
            var result = await _validator.TestValidateAsync(req);
            result.ShouldHaveValidationErrorFor(x => x.SecretKey);
            result.ShouldHaveValidationErrorFor(x => x.AccessKey);
            result.ShouldHaveValidationErrorFor(x => x.CloudStorageRegionEndPoint);
        }

        [Fact]
        public async Task Aws_Valid_Passes()
        {
            var req = new SaveStorageConfigurationRequest
            {
                Name = "cfg",
                StorageStrategy = "AWS",
                SecretKey = "s",
                AccessKey = "a",
                CloudStorageRegionEndPoint = "us-east-1"
            };
            var result = await _validator.TestValidateAsync(req);
            result.ShouldNotHaveValidationErrorFor(x => x.SecretKey);
        }

        [Fact]
        public async Task S3Compatible_MissingHost_Fails()
        {
            var req = new SaveStorageConfigurationRequest
            {
                Name = "cfg",
                StorageStrategy = "S3Compatible",
                SecretKey = "s",
                AccessKey = "a"
            };
            var result = await _validator.TestValidateAsync(req);
            result.ShouldHaveValidationErrorFor(x => x.Host);
        }

        [Fact]
        public async Task Sftp_MissingFields_Fails()
        {
            // RemoteBasePath is set to empty (not null) because the validator's
            // RemoteBasePath.Must(path => path.StartsWith('/')) rule has no cascade-stop
            // and throws NullReferenceException on a null value (latent source bug).
            var req = new SaveStorageConfigurationRequest
            {
                Name = "cfg",
                StorageStrategy = "SftpStorage",
                Host = "",
                UserName = "",
                Password = "",
                RemoteBasePath = ""
            };
            var result = await _validator.TestValidateAsync(req);
            result.ShouldHaveValidationErrorFor(x => x.Host);
            result.ShouldHaveValidationErrorFor(x => x.UserName);
            result.ShouldHaveValidationErrorFor(x => x.Password);
            result.ShouldHaveValidationErrorFor(x => x.RemoteBasePath);
        }

        [Fact]
        public async Task Sftp_Valid_Passes()
        {
            var req = new SaveStorageConfigurationRequest
            {
                Name = "cfg",
                StorageStrategy = "SftpStorage",
                Host = "sftp.example.com",
                UserName = "user",
                Password = "pass",
                RemoteBasePath = "/data"
            };
            var result = await _validator.TestValidateAsync(req);
            result.ShouldNotHaveValidationErrorFor(x => x.Host);
            result.ShouldNotHaveValidationErrorFor(x => x.RemoteBasePath);
        }

        [Fact]
        public async Task Sftp_RelativeBasePath_Fails()
        {
            var req = new SaveStorageConfigurationRequest
            {
                Name = "cfg",
                StorageStrategy = "SftpStorage",
                Host = "sftp.example.com",
                UserName = "user",
                Password = "pass",
                RemoteBasePath = "/data/../etc"
            };
            var result = await _validator.TestValidateAsync(req);
            result.ShouldHaveValidationErrorFor(x => x.RemoteBasePath);
        }

        [Fact]
        public async Task Sftp_ShortUserName_Fails()
        {
            var req = new SaveStorageConfigurationRequest
            {
                Name = "cfg",
                StorageStrategy = "SftpStorage",
                Host = "sftp.example.com",
                UserName = "ab",
                Password = "pass",
                RemoteBasePath = "/data"
            };
            var result = await _validator.TestValidateAsync(req);
            result.ShouldHaveValidationErrorFor(x => x.UserName);
        }

        [Fact]
        public async Task UpdateRequest_MissingItemId_Fails()
        {
            var req = ValidAzure();
            req.UpdateRequest = true;
            req.ItemId = "";
            var result = await _validator.TestValidateAsync(req);
            result.ShouldHaveValidationErrorFor(x => x.ItemId);
        }

        [Fact]
        public async Task UpdateRequest_ChangedNameNotUnique_Fails()
        {
            _repo.Setup(r => r.GetStorageConfigurationByIdAsync(It.IsAny<string>()))
                .ReturnsAsync(new StorageConfiguration { Name = "different-old" });
            _repo.Setup(r => r.GetStorageConfigurationByNameAsync(It.IsAny<string>()))
                .ReturnsAsync(new StorageConfiguration { Name = "cfg" });

            var req = ValidAzure();
            req.UpdateRequest = true;
            req.ItemId = "id1";
            var result = await _validator.TestValidateAsync(req);
            result.ShouldHaveValidationErrorFor(x => x.Name);
        }

        [Fact]
        public async Task CreateRequest_NonUniqueConnectionString_Fails()
        {
            _repo.Setup(r => r.GetStorageConfigurationByNameAsync(It.IsAny<string>()))
                .ReturnsAsync(new StorageConfiguration { Name = "existing" });

            var req = ValidAzure();
            var result = await _validator.TestValidateAsync(req);
            result.ShouldHaveValidationErrorFor(x => x.ConnectionString);
        }
    }
}
