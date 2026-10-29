using System.Net;
using System.Text.Json.Nodes;
using DomainService.Notification.Push;
using FluentAssertions;

namespace XUnitTest.Notification.Push
{
    /// <summary>The FCM v1 body per platform from the notification's options, the option limits, and the test result codes.</summary>
    public class FcmMessageBodyTests
    {
        private static readonly DateTimeOffset Now = new(2026, 10, 28, 12, 0, 0, TimeSpan.Zero);
        private static readonly IReadOnlyDictionary<string, string> NoData = new Dictionary<string, string>();

        private static readonly PushNotificationOptions Options = new()
        {
            WebLink = "https://shop.example.com/orders/{{orderId}}",
            Android = new AndroidPushOptions { Priority = "high", ChannelId = "orders", Sound = "ding", TtlSeconds = 3600, CollapseKey = "order-{orderId}", Color = "#1A73E8" },
            Ios = new IosPushOptions { Sound = "ding.caf", Badge = 3, Priority = "normal", InterruptionLevel = "time-sensitive", ThreadId = "orders", TtlSeconds = 86400 },
            Web = new WebPushOptions { Icon = "https://shop.example.com/icon.png", RequireInteraction = true, Urgency = "high", TtlSeconds = 0 },
        };

        private static JsonNode Body(string platform, PushPlatformBlocks? blocks) =>
            FcmMessageBody.Build(new FcmMessage("tok", platform, "T", "B", NoData, blocks), Now)["message"]!;

        private static PushPlatformBlocks Blocks() =>
            PushOptionRules.Resolve(Options, new Dictionary<string, string> { ["orderId"] = "42" })!;

        [Fact]
        public void Android_GetsPriorityTtlCollapseKeyAndNotificationOptions()
        {
            var android = Body("android", Blocks())["android"]!;

            android.ToJsonString().Should().Be(
                """{"priority":"HIGH","ttl":"3600s","collapse_key":"order-42","notification":{"channel_id":"orders","sound":"ding","color":"#1A73E8"}}""");
        }

        [Fact]
        public void Ios_GetsApnsHeadersAndAps()
        {
            var apns = Body("ios", Blocks())["apns"]!;

            apns["headers"]!["apns-priority"]!.GetValue<string>().Should().Be("5");
            apns["headers"]!["apns-expiration"]!.GetValue<string>().Should().Be(Now.AddDays(1).ToUnixTimeSeconds().ToString());
            apns["payload"]!["aps"]!.ToJsonString().Should().Be("""{"sound":"ding.caf","badge":3,"interruption-level":"time-sensitive","thread-id":"orders"}""");
        }

        [Fact]
        public void Web_GetsUrgencyTtlIconAndTheWebLink()
        {
            var webpush = Body("web", Blocks())["webpush"]!;

            webpush.ToJsonString().Should().Be(
                """{"headers":{"Urgency":"high","TTL":"0"},"notification":{"icon":"https://shop.example.com/icon.png","requireInteraction":true},"fcm_options":{"link":"https://shop.example.com/orders/42"}}""");
        }

        [Fact]
        public void WithoutOptions_TheMessageHasNoPlatformBlock()
        {
            var message = Body("ios", null).AsObject();

            message.Select(p => p.Key).Should().BeEquivalentTo("token", "notification", "data");
        }

        [Fact]
        public void BadStoredOptions_AreLeftOut_NeverSent()
        {
            var blocks = PushOptionRules.Resolve(new PushNotificationOptions
            {
                WebLink = "http://plain.example.com",
                Android = new AndroidPushOptions { Priority = "urgent", TtlSeconds = 99_999_999, ChannelId = "bad channel", Color = "blue" },
                Web = new WebPushOptions { Icon = "javascript:alert(1)" },
            }, NoData)!;

            Body("android", blocks).AsObject().ContainsKey("android").Should().BeFalse();
            Body("web", blocks).AsObject().ContainsKey("webpush").Should().BeFalse();
        }

        [Fact]
        public void ADraftWithBadOptions_GetsOneErrorPerField_WithoutTheValue()
        {
            var errors = PushOptionRules.Check(new PushNotificationOptions
            {
                Platforms = ["android", "windows"],
                WebLink = "http://secret-host.example.com",
                Ios = new IosPushOptions { Badge = 10_000, InterruptionLevel = "loud" },
                Web = new WebPushOptions { TtlSeconds = -1 },
            });

            errors.Keys.Should().BeEquivalentTo("platforms", "webLink", "ios.badge", "ios.interruptionLevel", "web.ttlSeconds");
            string.Join(" ", errors.Values).Should().NotContain("secret-host").And.NotContain("loud");
        }

        [Theory]
        [InlineData("android", FcmOutcome.Sent, 200, null, FcmErrorHint.None, PushTestCodes.Delivered)]
        [InlineData("android", FcmOutcome.InvalidToken, 404, "UNREGISTERED", FcmErrorHint.None, PushTestCodes.TokenExpired)]
        [InlineData("ios", FcmOutcome.Failed, 401, "THIRD_PARTY_AUTH_ERROR", FcmErrorHint.None, PushTestCodes.ApnsKeyMissing)]
        [InlineData("ios", FcmOutcome.Failed, 401, "THIRD_PARTY_AUTH_ERROR", FcmErrorHint.ApnsKeyRejected, PushTestCodes.ApnsKeyRejected)]
        [InlineData("web", FcmOutcome.Failed, 401, "THIRD_PARTY_AUTH_ERROR", FcmErrorHint.None, PushTestCodes.Failed)]
        [InlineData("android", FcmOutcome.Failed, 403, "SENDER_ID_MISMATCH", FcmErrorHint.None, PushTestCodes.WrongApp)]
        [InlineData("ios", FcmOutcome.Failed, 400, "INVALID_ARGUMENT", FcmErrorHint.WrongApp, PushTestCodes.WrongApp)]
        [InlineData("android", FcmOutcome.Failed, 403, "PERMISSION_DENIED", FcmErrorHint.None, PushTestCodes.KeyRefused)]
        [InlineData("android", FcmOutcome.AuthFailed, 401, null, FcmErrorHint.None, PushTestCodes.KeyRefused)]
        [InlineData("android", FcmOutcome.Failed, 404, "NOT_FOUND", FcmErrorHint.None, PushTestCodes.Failed)]
        public void TestResultCodes_FollowTheFcmAnswer(string platform, FcmOutcome outcome, int status, string? code, FcmErrorHint hint, string expected)
        {
            PushTestCodes.For(platform, new FcmSendResult(outcome, status, ErrorCode: code, Hint: hint)).Should().Be(expected);
        }

        [Theory]
        [InlineData("Auth error from APNS: InvalidProviderToken", FcmErrorHint.ApnsKeyRejected)]
        [InlineData("APNS returned Expired Provider Token", FcmErrorHint.ApnsKeyRejected)]
        [InlineData("APNs reason: DeviceTokenNotForTopic", FcmErrorHint.WrongApp)]
        [InlineData("Auth error from APNS or Web Push Service", FcmErrorHint.None)]
        public void TheErrorMessage_IsReducedToAFixedHint(string message, FcmErrorHint expected)
        {
            FcmClient.HintOf(message).Should().Be(expected);
        }

        [Fact]
        public void ABare404_IsAFailure_NotAnExpiredToken()
        {
            using var response = new HttpResponseMessage(HttpStatusCode.NotFound);

            FcmClient.Classify(response, FcmError.None).Outcome.Should().Be(FcmOutcome.Failed);
        }
    }
}
