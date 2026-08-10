namespace Common.InternalService.Storage
{
    public class GetPreSignedUrlForUploadRequest
    {
        /// <summary>
        /// command. ItemId: String representing the item ID.
        /// </summary>
        public string? ItemId { get; set; }
        /// <summary>
        /// command. MetaData: String representing abritrary structured data stored in file.
        /// </summary>
        public string MetaData { get; set; }
        /// <summary>
        /// command. Name: String representing the name of the file.
        /// </summary>
        public string Name { get; set; }
        /// <summary>
        /// command. ParentDirectoryId: String representing the parent directory ID of the file.
        /// </summary>
        public string ParentDirectoryId { get; set; }
        /// <summary>
        /// command. Tags: String representing the tags attached to the file.
        /// </summary>
        public string Tags { get; set; }
        /// <summary>
        /// command. AccessModifier: String representing the access modifier types available for the file.
        /// </summary>
        public string AccessModifier { get; set; } = "Private";

        public string? ConfigurationName { get; set; } = null;
        public ModuleName ModuleName { get; set; } = ModuleName.Default_Cloud;
        public Dictionary<string, string> AdditionalProperties { get; set; } = new Dictionary<string, string>();
    }
}
