namespace Mail.DomainService.Mails
{
    /// <summary>
    /// Query shape accepted by GET Mail/Gets so the existing client URL keeps working.
    /// Paging and tenantId are ignored; tenant isolation comes from the request DB context.
    /// </summary>
    public class GetMailConfigurationsRequest
    {
        public int PageNumber { get; set; }
        public int PageSize { get; set; }
    }
}
