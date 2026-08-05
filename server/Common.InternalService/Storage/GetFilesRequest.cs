namespace Common.InternalService.Storage
{
    public class GetFilesRequest
    {
        /// <summary>
        /// command. FileId: String representing the file ID.
        /// </summary>
        public string[] FileIds { get; set; }
        public string? ConfigurationName { get; set; } = null;
    }
}
