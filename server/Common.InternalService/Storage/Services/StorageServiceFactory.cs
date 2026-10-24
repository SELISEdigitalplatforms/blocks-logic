using Microsoft.Extensions.DependencyInjection;
using Blocks.Genesis.Entities.Data;

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
            // The configuration goes straight into the instance it is meant for. It used to be
            // copied into static fields first, which concurrent requests for different tenants
            // shared.
            var provider = configuration.StorageStrategy;

            return provider.ToLower() switch
            {
                "azure" => ActivatorUtilities.CreateInstance<AzureBlobStorageService>(_serviceProvider, configuration),
                "aws" => ActivatorUtilities.CreateInstance<AwsS3StorageService>(_serviceProvider, configuration),
                "sftpstorage" => ActivatorUtilities.CreateInstance<SftpStorageService>(_serviceProvider, configuration),
                "s3compatible" => ActivatorUtilities.CreateInstance<AwsS3CompatibleStorageService>(_serviceProvider, configuration),
                _ => throw new ArgumentException("Invalid storage provider", provider)
            };
        }
    }
}
