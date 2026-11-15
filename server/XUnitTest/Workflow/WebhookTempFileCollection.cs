namespace XUnitTest.Workflow
{
    /// <summary>Serializes the test classes that create webhook temp files (see their <c>[Collection]</c>).</summary>
    [CollectionDefinition(Name)]
    public sealed class WebhookTempFileCollection
    {
        public const string Name = "WebhookTempFiles";
    }
}
