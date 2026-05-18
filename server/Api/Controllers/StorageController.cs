using Blocks.Genesis;
using CloudConfiguration.DomainService.Shared.Services;
using CloudConfiguration.DomainService.Storage.Entities;
using CloudConfiguration.DomainService.Storage.RequestModel;
using DomainService.Storage;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Storage.DomainService.Services;
using Storage.DomainService.Storage;

namespace BlocksTemplate.Api.Controllers
{
    [ApiController]
    [Route("[controller]/[action]")]

    public class StorageController : ControllerBase
    {
        private readonly IConfigurationService _configurationService;
        
        private readonly IFileManagementService _fileManagementService;

        public StorageController(IConfigurationService configurationService,
                                 IFileManagementService fileManagementService)
        {
            _configurationService = configurationService;
            _fileManagementService = fileManagementService;
        }

        [HttpPost]
        //[ProtectedEndPoint]
        [Authorize]
        public async Task<BaseMutationResponse> Save([FromBody] SaveStorageConfigurationRequest request)
        {
           
            return await _configurationService.SaveStorageConfigurationAsync(request);
        }

        [HttpGet]
        //[ProtectedEndPoint]
        [Authorize]
        public async Task<List<StorageConfiguration>> Gets([FromQuery] GetStorageConfigurationsRequest request)
        {
            return await _configurationService.GetStorageConfigurationsAsync();
        }

        [HttpGet]
       // [ProtectedEndPoint]
        [Authorize]
        public async Task<StorageConfiguration> Get([FromQuery] GetStorageConfigurationRequest request)
        {
            return await _configurationService.GetStorageConfigurationAsync(request?.ConfigurationName ?? string.Empty);
        }

        [HttpPost]
       //[ProtectedEndPoint]
        public async Task<BaseResponse> Delete([FromQuery] DeleteStorageConfigurationRequest request)
        {
            
            return await _configurationService.DeleteStorageConfigurationAsync(request?.ConfigurationName ?? string.Empty);
        }

        [HttpPost]
        //[ProtectedEndPoint]
        [Authorize]
        public async Task<GetPreSignedUrlForUploadResponse> GetPreSignedUrlForUpload([FromBody] GetPreSignedUrlForUploadRequest request)
        {
           
            return await _fileManagementService.GetPerSignedUrlForUploadAsync(request);
        }

        [HttpGet]
        //[ProtectedEndPoint]
        [Authorize]
        public async Task<FileResponse?> GetFile([FromQuery] GetFileRequest request)
        {
           
            return await _fileManagementService.GetUrlForDownloadFileAsync(request);
        }

        [HttpPost]
        //[ProtectedEndPoint]
        [Authorize]
        public async Task<List<FileResponse>?> GetFiles([FromBody] GetFilesRequest request)
        {
            return await _fileManagementService.GetMultipleUrlsForDownloadFilesAsync(request);
        }

    }
}
