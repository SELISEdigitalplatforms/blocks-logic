namespace DomainService.PdfGenerator.service
{
    /// <summary>
    /// Notification service interface for PDF generator operations
    /// </summary>
    public interface IPdfGeneratorNotificationService
    {
        Task NotifyMergePdfsEvent(bool success, string outputPdfFileId, string messageCoRelationId, string? tenantId);
        Task NotifyCreatePdfsFromHtmlEvent(bool success, string messageCoRelationId, string? tenantId, int successCount, int failureCount);
        Task NotifyExtractTextFromPdfsEvent(bool success, string messageCoRelationId, string? tenantId);
        Task NotifyCreatePdfsFromHtmlUsingTEEvent(bool success, string messageCoRelationId, string? tenantId);
        Task NotifyCreatePdfsFromHtmlUsingTEBulkEvent(bool success, string messageCoRelationId, string? tenantId, int successCount, int failureCount);
        Task NotifyFixPdfsEvent(bool success, string messageCorrelationId, string? tenantId);
        Task NotifyStampImageToPdfEvent(bool success, string outputPdfFileId, string messageCoRelationId, string? tenantId);
        Task NotifyStampTextToPdfEvent(bool success, string outputPdfFileId, string messageCoRelationId, string? tenantId);
        Task NotifyStampIntoPdfEvent(bool success, string outputPdfFileId, string messageCoRelationId, string? tenantId);
    }
}


