using Blocks.Genesis;
using CloudConfiguration.DomainService.Shared.Services;
using CloudConfiguration.DomainService.Storage.Entities;
using CloudConfiguration.DomainService.Storage.RequestModel;
using Common.InternalService.Storage;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BlocksTemplate.Api.Controllers
{
    [ApiController]
    [Route("[controller]/[action]")]

    public class StorageController : ControllerBase
    {
        private readonly IConfigurationService _configurationService;

        private readonly IFileManagementService _fileManagementService;

        public StorageController(
            IConfigurationService configurationService,
                                 IFileManagementService fileManagementService)
        {
            _configurationService = configurationService;
            _fileManagementService = fileManagementService;
        }

        //[HttpPost]
        //[Authorize]
        //public async Task<BaseMutationResponse> Save([FromBody] SaveStorageConfigurationRequest request)
        //{

        //    return await _configurationService.SaveStorageConfigurationAsync(request);
        //}

        [HttpGet]
        [Authorize]
        public async Task<List<StorageConfiguration>> Gets ( [FromQuery] GetStorageConfigurationsRequest request )
            {
            return await _configurationService.GetStorageConfigurationsAsync();
            }

        [HttpGet]
        [Authorize]
        public async Task<StorageConfiguration> Get ( [FromQuery] GetStorageConfigurationRequest request )
            {
            return await _configurationService.GetStorageConfigurationAsync(request?.ConfigurationName ?? string.Empty);
            }

        //[HttpPost]
        //public async Task<BaseResponse> Delete([FromQuery] DeleteStorageConfigurationRequest request)
        //{

        //    return await _configurationService.DeleteStorageConfigurationAsync(request?.ConfigurationName ?? string.Empty);
        //}

        [HttpPost]
        [Authorize]
        public async Task<GetPreSignedUrlForUploadResponse> GetPreSignedUrlForUpload([FromBody] GetPreSignedUrlForUploadRequest request)
        {

            return await _fileManagementService.GetPerSignedUrlForUploadAsync(request);
        }

        [HttpGet]
        [Authorize]
        public async Task<FileResponse?> GetFile([FromQuery] GetFileRequest request)
        {

            return await _fileManagementService.GetUrlForDownloadFileAsync(request);
        }

        [HttpPost]
        [Authorize]
        public async Task<List<FileResponse>?> GetFiles([FromBody] GetFilesRequest request)
        {
            return await _fileManagementService.GetMultipleUrlsForDownloadFilesAsync(request);
        }

        //[HttpPost]
        //[Authorize]
        //public async Task<BaseResponse> DeleteFile([FromBody] DeleteFileRequest request)
        //{
        //    return await _fileManagementService.DeleteFileAsync(request);
        //}
    }
}
