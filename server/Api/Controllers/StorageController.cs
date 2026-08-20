using Blocks.Genesis;
using CloudConfiguration.DomainService.Shared.Services;
using CloudConfiguration.DomainService.Storage.Entities;
using CloudConfiguration.DomainService.Storage.RequestModel;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using DomainService.Storage.Dms;
using Storage.DomainService.Storage;
using StorageDirectoryManagementService = Storage.DomainService.Services.IFileDirectoryManagementService;
using StorageFileManagementService = Storage.DomainService.Services.IFileManagementService;
using DomainService.Storage;

namespace BlocksTemplate.Api.Controllers
{
    [ApiController]
    [Route("[controller]/[action]")]

    public class StorageController : ControllerBase
    {
        private readonly IConfigurationService _configurationService;

        private readonly StorageFileManagementService _fileManagementService;
        private readonly StorageDirectoryManagementService _directoryManagementService;

        public StorageController(
            IConfigurationService configurationService,
            StorageFileManagementService fileManagementService,
            StorageDirectoryManagementService directoryManagementService)
        {
            _configurationService = configurationService;
            _fileManagementService = fileManagementService;
            _directoryManagementService = directoryManagementService;
        }

        //[HttpPost]
        //[Authorize]
        //public async Task<BaseMutationResponse> Save([FromBody] SaveStorageConfigurationRequest request)
        //{

        //    return await _configurationService.SaveStorageConfigurationAsync(request);
        //}

        [HttpGet]
        [Authorize]
        public async Task<List<StorageConfiguration>> Gets([FromQuery] GetStorageConfigurationsRequest request)
        {
            return await _configurationService.GetStorageConfigurationsAsync();
        }

        [HttpGet]
        [Authorize]
        public async Task<StorageConfiguration> Get([FromQuery] GetStorageConfigurationRequest request)
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
        public async Task<GetPreSignedUrlForUploadResponse> GetPreSignedUrlForUpload(
            [FromBody] GetPreSignedUrlForUploadRequest request)
        {

            return await _fileManagementService.GetPerSignedUrlForUploadAsync(request);
        }

        [HttpGet]
        [Authorize]
        public async Task<FileResponse?> GetFile(
            [FromQuery] GetFileRequest request)
        {

            return await _fileManagementService.GetUrlForDownloadFileAsync(request);
        }

        [HttpPost]
        [Authorize]
        public async Task<List<FileResponse>?> GetFiles(
            [FromBody] GetFilesRequest request)
        {
            return await _fileManagementService.GetMultipleUrlsForDownloadFilesAsync(request);
        }

        [HttpPost]
        [Authorize]
        public async Task<BaseResponse> DeleteFile([FromBody] DomainService.Storage.DeleteFileRequest request)
        {
            return await _fileManagementService.DeleteFileAsync(request);
        }

        [HttpPost]
        [Authorize]
        public async Task<IActionResult> GetFilesInfo([FromBody] GetFilesInfoRequest request)
        {
            return Ok(await _fileManagementService.GetFilesInfoAsync(request));
        }

        [HttpPut]
        [Authorize]
        public async Task<IActionResult> UpdateFile([FromBody] UpdateFileRequest request)
        {
            return Ok(await _fileManagementService.UpdateFileAsync(request));
        }

        [HttpPost]
        [Authorize]
        public async Task<IActionResult> CreateDirectory([FromBody] CreateDirectoryRequest request)
        {
            return Ok(await _directoryManagementService.CreateDirectoryAsync(
                request.Name,
                request.ParentDirectoryId,
                request.Description,
                request.ConfigurationName,
                request.ModuleName?.ToString(),
                request.AllowedFileExtensions,
                HttpContext.RequestAborted));
        }

        [HttpPut]
        [Authorize]
        public async Task<IActionResult> UpdateDirectory([FromBody] UpdateDirectoryRequest request)
        {
            return Ok(await _directoryManagementService.UpdateDirectoryAsync(
                request.DirectoryId,
                request.Name,
                request.Description,
                HttpContext.RequestAborted));
        }

        [HttpPost]
        [Authorize]
        public async Task<IActionResult> DeleteDirectory([FromBody] DeleteDirectoryRequest request)
        {
            return Ok(await _directoryManagementService.DeleteDirectoryAsync(
                request.DirectoryId,
                request.Permanent,
                HttpContext.RequestAborted));
        }
    }
}
