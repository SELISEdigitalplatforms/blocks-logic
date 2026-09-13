using Blocks.Genesis;
using CloudConfiguration.DomainService.Shared.Services;
using CloudConfiguration.DomainService.Storage.Entities;
using CloudConfiguration.DomainService.Storage.RequestModel;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using DomainService.Storage;
using StorageDriver;

namespace BlocksTemplate.Api.Controllers
{
    /// <summary>
    /// Storage-provider configuration plus the pre-signed URL flow for uploads and downloads, routed as
    /// <c>/api/Storage/{action}</c>. File bytes never pass through this API — callers get a URL and talk
    /// to the provider directly.
    /// </summary>
    [ApiController]
    [Route("[controller]/[action]")]

    public class StorageController : ControllerBase
    {
        private readonly IConfigurationService _configurationService;
        private readonly IStorageDriverService _storageDriverService;

        /// <summary>Takes the configuration service and the storage driver.</summary>
        public StorageController(
            IConfigurationService configurationService,
            IStorageDriverService storageDriverService)
        {
            _configurationService = configurationService;
            _storageDriverService = storageDriverService;
        }

        /// <summary><c>POST</c> — creates or updates a storage-provider configuration.</summary>
        [HttpPost]
        [Authorize]
        public async Task<BaseMutationResponse> Save([FromBody] SaveStorageConfigurationRequest request)
        {

           return await _configurationService.SaveStorageConfigurationAsync(request);
        }

        /// <summary>
        /// <c>GET</c> — every storage configuration for the tenant. The request object is accepted for
        /// signature symmetry and is deliberately unused.
        /// </summary>
        [HttpGet]
        [Authorize]
        public async Task<List<StorageConfiguration>> Gets([FromQuery] GetStorageConfigurationsRequest request)
        {
            return await _configurationService.GetStorageConfigurationsAsync();
        }

        /// <summary><c>GET</c> — one storage configuration.</summary>
        [HttpGet]
        [Authorize]
        public async Task<StorageConfiguration> Get([FromQuery] GetStorageConfigurationRequest request)
        {
           return await _configurationService.GetStorageConfigurationAsync(request?.ConfigurationName ?? string.Empty);
        }

        /// <summary><c>DELETE</c> — removes a storage configuration.</summary>
        [HttpPost]
        public async Task<BaseResponse> Delete([FromQuery] DeleteStorageConfigurationRequest request)
        {

           return await _configurationService.DeleteStorageConfigurationAsync(request?.ConfigurationName ?? string.Empty);
        }

        /// <summary><c>POST</c> — a pre-signed URL the client uploads to directly.</summary>
        [HttpPost]
        [Authorize]
        public async Task<GetPreSignedUrlForUploadResponse> GetPreSignedUrlForUpload(
            [FromBody] GetPreSignedUrlForUploadRequest request)
        {

            return await _storageDriverService.GetPerSignedUrlForUploadAsync(request);
        }

        /// <summary><c>GET</c> — a pre-signed download URL for one file.</summary>
        [HttpGet]
        [Authorize]
        public async Task<FileResponse?> GetFile(
            [FromQuery] GetFileRequest request)
        {

            return await _storageDriverService.GetUrlForDownloadFileAsync(request);
        }

        /// <summary><c>POST</c> — pre-signed download URLs for several files in one call.</summary>
        [HttpPost]
        [Authorize]
        public async Task<List<FileResponse>?> GetFiles(
            [FromBody] GetFilesRequest request)
        {
            return await _storageDriverService.GetMultipleUrlsForDownloadFileAsync(request);
        }

        /// <summary><c>DELETE</c> via <c>POST</c> — removes a file from the provider.</summary>
        [HttpPost]
        [Authorize]
        public async Task<BaseResponse> DeleteFile([FromBody] DeleteFileRequest request)
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
