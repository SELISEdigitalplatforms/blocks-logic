using Blocks.Genesis;

namespace Common.InternalService.Storage
{
    public class GetPreSignedUrlForUploadResponse : BaseResponse
    {
        public string UploadUrl { get; set; } = string.Empty;
        public string FileId { get; set; } = string.Empty;
    }
}
