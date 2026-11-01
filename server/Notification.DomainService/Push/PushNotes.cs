namespace DomainService.Notification.Push
{
    /// <summary>Title, body and app link of one push, already rendered.</summary>
    public sealed record PushText(string Title, string Body, string? AppLink);

    /// <summary>Builds the note of one device, the same way for real sends and tests.</summary>
    public static class PushNotes
    {
        public const string LinkKey = "link";

        /// <summary>
        /// <c>data.link</c> = the app link. With options (<paramref name="blocks"/>), web devices get no app link: their
        /// click opens the options' web link (<c>webpush.fcm_options.link</c>). Without options every device gets it, as before.
        /// </summary>
        public static PushNote Create(PushDb db, NotificationDevice device, PushText text, IReadOnlyDictionary<string, string> data,
            PushPlatformBlocks? blocks)
        {
            ArgumentNullException.ThrowIfNull(device);
            ArgumentNullException.ThrowIfNull(text);
            var appLink = blocks is not null && device.Platform == PushPlatforms.Web ? null : text.AppLink;
            var deviceData = appLink is null ? data : new Dictionary<string, string>(data) { [LinkKey] = appLink };
            return new PushNote(db, device, text.Title, text.Body, deviceData, blocks);
        }
    }
}
