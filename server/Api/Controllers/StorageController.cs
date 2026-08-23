using Blocks.Genesis;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using DomainService.Storage;
using StorageDriver;

namespace BlocksTemplate.Api.Controllers
{
    [ApiController]
    [Route("[controller]/[action]")]

    public class StorageController : ControllerBase
    {
        private readonly IStorageDriverService _storageDriverService;

        public StorageController(IStorageDriverService storageDriverService)
        {
            _storageDriverService = storageDriverService;
        }

        //[HttpPost]
        //[Authorize]
        //public async Task<BaseMutationResponse> Save([FromBody] SaveStorageConfigurationRequest request)
        //{

        //    return await _configurationService.SaveStorageConfigurationAsync(request);
        //}

        //[HttpGet]
        //[Authorize]
        //public async Task<List<StorageConfiguration>> Gets([FromQuery] GetStorageConfigurationsRequest request)
        //{
        //    return await _configurationService.GetStorageConfigurationsAsync();
        //}

        //[HttpGet]
        //[Authorize]
        //public async Task<StorageConfiguration> Get([FromQuery] GetStorageConfigurationRequest request)
        //{
        //    return await _configurationService.GetStorageConfigurationAsync(request?.ConfigurationName ?? string.Empty);
        //}

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

            return await _storageDriverService.GetPerSignedUrlForUploadAsync(request);
        }

        [HttpGet]
        [Authorize]
        public async Task<FileResponse?> GetFile(
            [FromQuery] GetFileRequest request)
        {

            return await _storageDriverService.GetUrlForDownloadFileAsync(request);
        }

        [HttpPost]
        [Authorize]
        public async Task<List<FileResponse>?> GetFiles(
            [FromBody] GetFilesRequest request)
        {
            return await _storageDriverService.GetMultipleUrlsForDownloadFileAsync(request);
        }

        [HttpPost]
        [Authorize]
        public async Task<BaseResponse> DeleteFile([FromBody] DomainService.Storage.DeleteFileRequest request)
        {
            return await _storageDriverService.DeleteFileAsync(request);
        }

        //[HttpPost]
        //[Authorize]
        //public async Task<IActionResult> GetFilesInfo([FromBody] GetFilesInfoRequest request)
        //{
        //    return Ok(await _fileManagementService.GetFilesInfoAsync(request));
        //}

        //[HttpPut]
        //[Authorize]
        //public async Task<IActionResult> UpdateFile([FromBody] UpdateFileRequest request)
        //{
        //    return Ok(await _fileManagementService.UpdateFileAsync(request));
        //}

        //[HttpPost]
        //[Authorize]
        //public async Task<IActionResult> CreateDirectory([FromBody] CreateDirectoryRequest request)
        //{
        //    return Ok(await _directoryManagementService.CreateDirectoryAsync(
        //        request.Name,
        //        request.ParentDirectoryId,
        //        request.Description,
        //        request.ConfigurationName,
        //        request.ModuleName?.ToString(),
        //        request.AllowedFileExtensions,
        //        HttpContext.RequestAborted));
        //}

        //[HttpPut]
        //[Authorize]
        //public async Task<IActionResult> UpdateDirectory([FromBody] UpdateDirectoryRequest request)
        //{
        //    return Ok(await _directoryManagementService.UpdateDirectoryAsync(
        //        request.DirectoryId,
        //        request.Name,
        //        request.Description,
        //        HttpContext.RequestAborted));
        //}

        //[HttpPost]
        //[Authorize]
        //public async Task<IActionResult> DeleteDirectory([FromBody] DeleteDirectoryRequest request)
        //{
        //    return Ok(await _directoryManagementService.DeleteDirectoryAsync(
        //        request.DirectoryId,
        //        request.Permanent,
        //        HttpContext.RequestAborted));
        //}
    }
}
