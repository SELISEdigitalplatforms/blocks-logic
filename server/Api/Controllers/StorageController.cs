using Blocks.Genesis;
using Microsoft.AspNetCore.Mvc;
using DomainService.Storage;
using StorageDriver;

namespace BlocksTemplate.Api.Controllers
{
    /// <summary>
    /// The pre-signed URL flow for uploads and downloads, routed as <c>/api/Storage/{action}</c>. File bytes
    /// never pass through this API — callers get a URL and talk to the provider directly. Storage-provider
    /// configuration lives in blocks-os (<c>/Storage/Save|Gets|Get|Delete</c>); logic only reads it.
    /// </summary>
    [ApiController]
    [Route("[controller]/[action]")]

    public class StorageController : ControllerBase
    {
        private readonly IStorageDriverService _storageDriverService;

        /// <summary>Takes the storage driver.</summary>
        public StorageController(IStorageDriverService storageDriverService)
        {
            _storageDriverService = storageDriverService;
        }

        /// <summary><c>POST</c> — a pre-signed URL the client uploads to directly.</summary>
        [HttpPost]
        [ProtectedEndPoint("blocks-logic::storage-file::manage")]
        public async Task<GetPreSignedUrlForUploadResponse> GetPreSignedUrlForUpload(
            [FromBody] GetPreSignedUrlForUploadRequest request)
        {

            return await _storageDriverService.GetPerSignedUrlForUploadAsync(request);
        }

        /// <summary>
        /// <c>POST</c> — compatibility route for clients that cannot call the blocks-data API
        /// directly. Delegates to the storage driver, which forwards to blocks-data's own
        /// verification pipeline; this route does not duplicate any completion/verification logic.
        /// </summary>
        [HttpPost]
        [ProtectedEndPoint("blocks-logic::storage-file::manage")]
        public async Task<CompleteUploadResponse> CompleteUpload([FromBody] CompleteUploadRequest request)
        {
            return await _storageDriverService.CompleteUploadAsync(request);
        }

        /// <summary><c>GET</c> — a pre-signed download URL for one file.</summary>
        [HttpGet]
        [ProtectedEndPoint("blocks-logic::storage-file::read")]
        public async Task<FileResponse?> GetFile(
            [FromQuery] GetFileRequest request)
        {

            return await _storageDriverService.GetUrlForDownloadFileAsync(request);
        }

        /// <summary><c>POST</c> — pre-signed download URLs for several files in one call.</summary>
        [HttpPost]
        [ProtectedEndPoint("blocks-logic::storage-file::read")]
        public async Task<List<FileResponse>?> GetFiles(
            [FromBody] GetFilesRequest request)
        {
            return await _storageDriverService.GetMultipleUrlsForDownloadFileAsync(request);
        }

        /// <summary><c>DELETE</c> via <c>POST</c> — removes a file from the provider.</summary>
        [HttpPost]
        [ProtectedEndPoint("blocks-logic::storage-file::manage")]
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
