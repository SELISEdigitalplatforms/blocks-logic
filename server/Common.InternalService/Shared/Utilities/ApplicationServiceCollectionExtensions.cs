using Common.InternalService.Language;
using Common.InternalService.Monitor;
using Common.InternalService.Secret.Services;
using Common.InternalService.Storage;
using CloudConfiguration.DomainService.Shared.Services;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;

namespace Common.InternalService.Shared.Utilities
{
    public static class ApplicationServiceCollectionExtensions
    {
        public static void RegisterCommonInternalServices(this IServiceCollection serviceCollection)
        {
            #region Language
            serviceCollection.AddSingleton<ILanguageManagementService, LanguageManagementService>();
            serviceCollection.AddSingleton<ILanguageRepository, LanguageRepository>();
            #endregion

            #region Storage
            serviceCollection.AddSingleton<IConfigurationRepository, ConfigurationRepository>();

            serviceCollection.AddSingleton<IFileManagementService, FileManagementService>();
            serviceCollection.AddSingleton<ICertificateStorageService, CertificateStorageService>();
            serviceCollection.AddSingleton<IFileRepository, FileRepository>();
            serviceCollection.AddSingleton<IFileVersionRepository, FileVersionRepository>();
            serviceCollection.AddSingleton<IDirectoryRepository, DirectoryRepository>();
            serviceCollection.AddSingleton<IContentAccessRepository, ContentAccessRepository>();
            serviceCollection.AddSingleton<IContentAccessResolver, ContentAccessResolver>();
            serviceCollection.AddSingleton<IStorageServiceFactory, StorageServiceFactory>();

            serviceCollection.AddTransient<AzureBlobStorageService>();
            serviceCollection.AddTransient<AwsS3StorageService>();
            serviceCollection.AddTransient<AwsS3CompatibleStorageService>();
            serviceCollection.AddTransient<SftpStorageService>();

            serviceCollection.AddTransient<IValidator<GetPreSignedUrlForUploadRequest>, GetPreSignedUrlForUploadRequestValidator>();
            #endregion

            #region Monitor
            serviceCollection.AddSingleton<IMonitorObservabilityService, MonitorObservabilityService>();
            serviceCollection.AddSingleton<IMonitorConfigurationService, MonitorConfigurationService>();
            serviceCollection.AddSingleton<IMonitorConfigurationRepoService, MonitorConfigurationRepoService>();
            serviceCollection.AddSingleton<IMonitorIncidentService, MonitorIncidentService>();
            serviceCollection.AddSingleton<IMonitorIncidentRepoService, MonitorIncidentRepoService>();
            serviceCollection.AddSingleton<IMonitorPingService, MonitorPingService>();
            serviceCollection.AddSingleton<IMonitorPingRepoService, MonitorPingRepoService>();

            serviceCollection.AddTransient<IValidator<SaveMonitorConfigurationRequest>, SaveMonitorConfigurationRequestValidator>();
            serviceCollection.AddTransient<IValidator<UpdateMonitorConfigurationRequest>, UpdateMonitorConfigurationRequestValidator>();
            #endregion

            #region Secret
            // Read-only catalog of the tenant's platform secrets, shared by every module that offers a secret
            // picker (Proxy, Workflow, ...). SeliseBlocks.Secrets.OS has no HTTP surface of its own, so this
            // thin control-plane seam over the in-process ISecretService is what GET /api/Secret/GetAll calls.
            // ISecretService itself is supplied by AddBlocksSecrets() in Program.cs.
            serviceCollection.AddSingleton<ISecretCatalogService, SecretCatalogService>();
            #endregion
        }
    }
}
