namespace DomainService.Notification.Push
{
    /// <summary>One device of a test: what happened and whether the device was removed (expired token).</summary>
    public sealed record PushTestDevice(string Platform, string? Name, DateTime LastSeenAt, string Result, bool Removed);

    /// <summary>Answer of <c>POST Notifier/push-test</c> and <c>POST Notifier/push-test/notification</c>.</summary>
    public sealed class PushTestResult
    {
        public const string PushField = "push";
        public const string PlatformField = "platform";
        public const string DevicesField = "devices";
        public const string PushOff = "Mobile push is off or not set up.";
        public const string PlatformOff = "This platform is turned off.";
        public const string NoDevice = "No device is registered for your user.";

        public bool IsSuccess { get; init; }
        public IReadOnlyDictionary<string, string>? Errors { get; init; }
        public DateTime? SentAt { get; init; }
        public int Delivered { get; init; }
        public int Failed { get; init; }
        public IReadOnlyList<PushTestDevice> Devices { get; init; } = [];

        public static PushTestResult Refused(string field, string message) =>
            Refused(new Dictionary<string, string> { [field] = message });

        public static PushTestResult Refused(IReadOnlyDictionary<string, string> errors) => new() { IsSuccess = false, Errors = errors };
    }

    /// <summary>
    /// Result codes of a test, from what FCM answered (contract §4). UNREGISTERED → tokenExpired; THIRD_PARTY_AUTH_ERROR
    /// on iOS → apnsKeyRejected when APNs named the provider token, else apnsKeyMissing; SENDER_ID_MISMATCH or a
    /// wrong-app APNs reason → wrongApp; 403 / PERMISSION_DENIED / 401 after renewal → keyRefused; else failed.
    /// </summary>
    public static class PushTestCodes
    {
        public const string Delivered = "delivered";
        public const string ApnsKeyMissing = "apnsKeyMissing";
        public const string ApnsKeyRejected = "apnsKeyRejected";
        public const string WrongApp = "wrongApp";
        public const string TokenExpired = "tokenExpired";
        public const string KeyRefused = "keyRefused";
        public const string Failed = "failed";

        public static string For(string platform, FcmSendResult result)
        {
            ArgumentNullException.ThrowIfNull(result);
            if (result.Outcome == FcmOutcome.Sent)
                return Delivered;
            if (result.Outcome == FcmOutcome.InvalidToken)
                return TokenExpired;
            if (result.ErrorCode == "THIRD_PARTY_AUTH_ERROR")
                return ThirdPartyAuth(platform, result.Hint);
            if (result.ErrorCode == "SENDER_ID_MISMATCH" || result.Hint == FcmErrorHint.WrongApp)
                return WrongApp;
            if (result.Outcome == FcmOutcome.AuthFailed || result.Status == 403 || result.ErrorCode == "PERMISSION_DENIED")
                return KeyRefused;
            return Failed;
        }

        private static string ThirdPartyAuth(string platform, FcmErrorHint hint)
        {
            if (platform != PushPlatforms.Ios)
                return Failed;
            return hint == FcmErrorHint.ApnsKeyRejected ? ApnsKeyRejected : ApnsKeyMissing;
        }
    }
}
