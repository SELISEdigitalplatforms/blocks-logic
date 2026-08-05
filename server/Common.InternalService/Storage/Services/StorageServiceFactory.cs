using CloudConfiguration.DomainService.Storage.Entities;
using Microsoft.Extensions.DependencyInjection;

namespace Common.InternalService.Storage
{
    public interface IStorageServiceFactory
    {
        IStorageService GetStorageService(StorageConfiguration configuration);
    }

    public class StorageServiceFactory : IStorageServiceFactory
    {
        private readonly IServiceProvider _serviceProvider;

        public StorageServiceFactory(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
        }

        public IStorageService GetStorageService(StorageConfiguration configuration)
        {
            try
            {
                StorageProvider.ConnectionString = configuration.ConnectionString;
                StorageProvider.SecretKey = configuration.SecretKey;
                StorageProvider.AccessKey = configuration.AccessKey;
                StorageProvider.Region = configuration.CloudStorageRegionEndPoint;

                #region Local Storage
                StorageProvider.Host = configuration.Host;
                StorageProvider.Port = configuration.Port;
                StorageProvider.UserName = configuration.UserName;
                StorageProvider.Password = configuration.Password;
                StorageProvider.RemoteBasePath = configuration.RemoteBasePath;
                StorageProvider.SftpSecretKey = configuration.SftpSecretKey;
                #endregion

                var provider = configuration.StorageStrategy;

                return provider.ToLower() switch
                {
                    "azure" => _serviceProvider.GetRequiredService<AzureBlobStorageService>(),
                    "aws" => _serviceProvider.GetRequiredService<AwsS3StorageService>(),
                    "sftpstorage" => _serviceProvider.GetRequiredService<SftpStorageService>(),
                    "s3compatible" => _serviceProvider.GetRequiredService<AwsS3CompatibleStorageService>(),
                    _ => throw new ArgumentException("Invalid storage provider", provider)
                };
            }
            finally
            {
                StorageProvider.ConnectionString = string.Empty;
                StorageProvider.SecretKey = string.Empty;
                StorageProvider.AccessKey = string.Empty;
                StorageProvider.Region = string.Empty;

                #region Local Storage
                StorageProvider.Host = string.Empty;
                StorageProvider.Port = string.Empty;
                StorageProvider.UserName = string.Empty;
                StorageProvider.Password = string.Empty;
                StorageProvider.RemoteBasePath = string.Empty;
                StorageProvider.SftpSecretKey = string.Empty;
                #endregion
            }
        }
    }
}
