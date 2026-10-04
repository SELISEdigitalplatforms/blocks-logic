
namespace CloudConfiguration.DomainService.Shared.Enums
{
    /// <summary>
    /// Mirrors blocks-os Configuration.DomainService.Shared.Enums.MailServiceProvider. Values are
    /// explicit because they are persisted; appending is safe, reordering is not.
    /// </summary>
    public enum MailServiceProvider
    {
        AmazonSes = 0,
        Zoho = 1,
        Office365Smtp = 2,
        Gmail = 3
    }
}
