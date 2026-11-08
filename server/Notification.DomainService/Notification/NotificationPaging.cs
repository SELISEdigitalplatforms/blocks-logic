namespace DomainService.Notification
{
    /// <summary>
    /// The paging rule for <c>GetNotifications</c>: Page 0 or greater, PageSize 1 to <see cref="MaxPageSize"/>, and
    /// Page × PageSize within an int, because the Mongo skip is an int. Out-of-range values used to reach the
    /// repository unchecked and fail there (an OverflowException or a negative skip).
    /// </summary>
    public static class NotificationPaging
    {
        public const int MaxPageSize = 100;

        /// <summary>One message per failing parameter, keyed by its name, or null when the paging is valid.</summary>
        public static Dictionary<string, string>? Validate(GetNotificationsRequest request)
        {
            ArgumentNullException.ThrowIfNull(request);

            var errors = new Dictionary<string, string>();
            var pageIsValid = request.Page >= 0;
            var pageSizeIsValid = request.PageSize is >= 1 and <= MaxPageSize;

            if (!pageIsValid)
            {
                errors["Page"] = "Page must be 0 or greater.";
            }

            if (!pageSizeIsValid)
            {
                errors["PageSize"] = $"PageSize must be between 1 and {MaxPageSize}.";
            }

            // Judged only against a valid PageSize, so a bad PageSize is reported as itself.
            if (pageIsValid && pageSizeIsValid && (long)request.Page * request.PageSize > int.MaxValue)
            {
                errors["Page"] = "Page is too large for this PageSize.";
            }

            return errors.Count == 0 ? null : errors;
        }
    }
}
