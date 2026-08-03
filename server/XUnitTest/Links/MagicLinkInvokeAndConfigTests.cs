using Blocks.Genesis;
using DomainService.MagicLink;
using DomainService.MagicLink.Events;
using DomainService.MagicLink.Models;
using DomainService.MagicLink.Service;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using MagicLinkEntity = DomainService.MagicLink.Models.MagicLink;

namespace XUnitTest.Links
{
    /// <summary>
    /// Unit tests for the invoke and link-based-action-config paths of
    /// <see cref="MagicLinkService"/>, which the existing MagicLinkServiceTests do not reach.
    ///
    /// Invocation is the public, unauthenticated edge of this feature: whoever holds the short
    /// code gets whatever the link grants. The refusal reasons are therefore treated as the
    /// contract here, including the order they are evaluated in, because a guard that moves
    /// changes which links still work.
    /// </summary>
    public class MagicLinkInvokeAndConfigTests
    {
        private readonly Mock<IMagicLinkRepository> _repo = new();
        private readonly Mock<ICacheClient> _cache = new();
        private readonly Mock<IMessageClient> _messages = new();
        private readonly MagicLinkService _sut;

        public MagicLinkInvokeAndConfigTests()
        {
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["RootTenantId"] = "root-tenant",
                    ["MagicLinkBaseAddress"] = "https://links.example.com/",
                })
                .Build();

            _sut = new MagicLinkService(
                NullLogger<MagicLinkService>.Instance,
                _repo.Object,
                _cache.Object,
                _messages.Object,
                configuration);
        }

        private static MagicLinkEntity Link(
            MagicLinkType type = MagicLinkType.Redirect,
            bool isExpired = false,
            string? expiredReason = null,
            DateTime? expiryDate = null,
            int usageLimit = 0,
            int usageCount = 0,
            string uri = "https://target.example.com/page",
            string? redirectUrl = null) => new()
            {
                ItemId = "link-1",
                ProjectKey = "project-1",
                Type = type,
                IsExpired = isExpired,
                ExpiredReason = expiredReason,
                ExpiryDate = expiryDate,
                UsageLimit = usageLimit,
                UsageCount = usageCount,
                Uri = uri,
                RedirectUrl = redirectUrl,
            };

        private void StoredLink(MagicLinkEntity? link) =>
            _repo.Setup(r => r.GetMagicLinkAsync(It.IsAny<string>(), It.IsAny<string?>()))
                 .ReturnsAsync(link);

        private static InvokeMagicLinkRequest InvokeRequest() => new()
        {
            LinkId = "link-1",
            ProjectKey = "project-1",
            VisitorIpAddress = "203.0.113.7",
        };

        #region InvokeLinkAsync

        [Fact]
        public async Task An_unknown_link_is_refused_rather_than_treated_as_empty()
        {
            StoredLink(null);

            var response = await _sut.InvokeLinkAsync(InvokeRequest());

            response.IsSuccess.Should().BeFalse();
            response.ErrorCode.Should().Be("LINK_NOT_FOUND");
            response.RedirectUrl.Should().BeNull();
        }

        [Fact]
        public async Task An_explicitly_expired_link_reports_the_stored_reason()
        {
            StoredLink(Link(isExpired: true, expiredReason: "Revoked"));

            var response = await _sut.InvokeLinkAsync(InvokeRequest());

            response.IsSuccess.Should().BeFalse();
            response.ErrorCode.Should().Be("LINK_EXPIRED");
            response.ErrorMessage.Should().Contain("Revoked");
        }

        [Fact]
        public async Task A_link_past_its_expiry_date_is_refused()
        {
            StoredLink(Link(expiryDate: DateTime.UtcNow.AddMinutes(-1)));

            var response = await _sut.InvokeLinkAsync(InvokeRequest());

            response.IsSuccess.Should().BeFalse();
            response.ErrorCode.Should().Be("LINK_EXPIRED");
        }

        [Fact]
        public async Task A_link_still_inside_its_expiry_date_is_honoured()
        {
            // The comparison is strict, so a future date must not read as expired.
            StoredLink(Link(expiryDate: DateTime.UtcNow.AddMinutes(5)));

            var response = await _sut.InvokeLinkAsync(InvokeRequest());

            response.IsSuccess.Should().BeTrue();
        }

        [Fact]
        public async Task A_link_that_reached_its_usage_limit_is_refused()
        {
            StoredLink(Link(usageLimit: 3, usageCount: 3));

            var response = await _sut.InvokeLinkAsync(InvokeRequest());

            response.IsSuccess.Should().BeFalse();
            response.ErrorCode.Should().Be("LINK_LIMIT_EXCEEDED");
        }

        [Fact]
        public async Task A_usage_limit_of_zero_means_unlimited_rather_than_none()
        {
            // The guard is `UsageLimit > 0 && ...`. If that ever becomes `>= 0`, every link
            // with the default limit stops working, which this pins.
            StoredLink(Link(usageLimit: 0, usageCount: 500));

            var response = await _sut.InvokeLinkAsync(InvokeRequest());

            response.IsSuccess.Should().BeTrue();
        }

        [Fact]
        public async Task The_expiry_check_is_evaluated_before_the_usage_limit()
        {
            // Both conditions hold. The reported code tells an operator which rule actually
            // stopped the request, so the order is part of the contract.
            StoredLink(Link(isExpired: true, expiredReason: "Revoked", usageLimit: 1, usageCount: 9));

            var response = await _sut.InvokeLinkAsync(InvokeRequest());

            response.ErrorCode.Should().Be("LINK_EXPIRED");
        }

        [Fact]
        public async Task A_redirect_link_returns_its_target_uri()
        {
            StoredLink(Link(type: MagicLinkType.Redirect, uri: "https://target.example.com/welcome"));

            var response = await _sut.InvokeLinkAsync(InvokeRequest());

            response.IsSuccess.Should().BeTrue();
            response.Type.Should().Be(nameof(MagicLinkType.Redirect));
            response.RedirectUrl.Should().Be("https://target.example.com/welcome");
        }

        [Fact]
        public async Task An_action_link_queues_the_action_and_returns_its_post_action_url()
        {
            StoredLink(Link(
                type: MagicLinkType.Action,
                uri: "https://target.example.com/do",
                redirectUrl: "https://target.example.com/done"));

            var response = await _sut.InvokeLinkAsync(InvokeRequest());

            response.IsSuccess.Should().BeTrue();
            response.Type.Should().Be(nameof(MagicLinkType.Action));
            response.RedirectUrl.Should().Be("https://target.example.com/done",
                "an action link redirects to its completion page, not to the action uri");

            _messages.Verify(m => m.SendToConsumerAsync(
                It.IsAny<ConsumerMessage<MagicLinkActionEvent>>()), Times.Once);
        }

        [Fact]
        public async Task An_action_link_carries_the_visitor_details_onto_the_queued_event()
        {
            ConsumerMessage<MagicLinkActionEvent>? captured = null;
            _messages.Setup(m => m.SendToConsumerAsync(It.IsAny<ConsumerMessage<MagicLinkActionEvent>>()))
                     .Callback<ConsumerMessage<MagicLinkActionEvent>>(m => captured = m)
                     .Returns(Task.CompletedTask);
            StoredLink(Link(type: MagicLinkType.Action));

            await _sut.InvokeLinkAsync(InvokeRequest());

            captured.Should().NotBeNull();
            captured!.Payload.LinkId.Should().Be("link-1");
            captured.Payload.ProjectKey.Should().Be("project-1");
            captured.Payload.VisitorIpAddress.Should().Be("203.0.113.7");
        }

        [Fact]
        public async Task A_repository_failure_is_reported_on_the_response_rather_than_thrown()
        {
            // Invocation sits behind a public endpoint, so an unhandled throw here would be a
            // stack trace to an anonymous caller. The service catches and reports instead.
            _repo.Setup(r => r.GetMagicLinkAsync(It.IsAny<string>(), It.IsAny<string?>()))
                 .ThrowsAsync(new InvalidOperationException("mongo unreachable"));

            var response = await _sut.InvokeLinkAsync(InvokeRequest());

            response.IsSuccess.Should().BeFalse();
            response.ErrorMessage.Should().Contain("mongo unreachable");
        }

        #endregion

        #region SaveLinkBasedActionConfigAsync

        private static SaveLinkBasedActionConfigRequest ConfigRequest(string? projectKey = "project-1") => new()
        {
            ContextName = "orders",
            ShortUrlBase = "https://s.example.com",
            ProjectKey = projectKey,
        };

        [Fact]
        public async Task A_project_without_a_config_gets_one_created()
        {
            _repo.Setup(r => r.GetLinkBasedActionConfigAsync(It.IsAny<string>()))
                 .ReturnsAsync((LinkBasedActionConfig?)null);

            var response = await _sut.SaveLinkBasedActionConfigAsync(ConfigRequest());

            response.IsSuccess.Should().BeTrue();
            response.WasCreated.Should().BeTrue();
            response.ConfigId.Should().NotBeNullOrWhiteSpace();
            response.Config!.ContextName.Should().Be("orders");
            response.Config.ProjectKey.Should().Be("project-1");
            _repo.Verify(r => r.CreateLinkBasedActionConfigAsync(It.IsAny<LinkBasedActionConfig>()), Times.Once);
        }

        [Fact]
        public async Task An_omitted_project_key_falls_back_to_the_root_tenant()
        {
            LinkBasedActionConfig? created = null;
            _repo.Setup(r => r.GetLinkBasedActionConfigAsync(It.IsAny<string>()))
                 .ReturnsAsync((LinkBasedActionConfig?)null);
            _repo.Setup(r => r.CreateLinkBasedActionConfigAsync(It.IsAny<LinkBasedActionConfig>()))
                 .Callback<LinkBasedActionConfig>(c => created = c)
                 .ReturnsAsync("created");

            await _sut.SaveLinkBasedActionConfigAsync(ConfigRequest(projectKey: null));

            created!.ProjectKey.Should().Be("root-tenant");
        }

        [Fact]
        public async Task An_existing_config_is_updated_in_place_rather_than_duplicated()
        {
            var existing = new LinkBasedActionConfig
            {
                ItemId = "config-1",
                ContextName = "old-context",
                ShortUrlBase = "https://old.example.com",
                ProjectKey = "project-1",
                CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            };
            _repo.Setup(r => r.GetLinkBasedActionConfigAsync(It.IsAny<string>())).ReturnsAsync(existing);
            _repo.Setup(r => r.UpdateLinkBasedActionConfigAsync(It.IsAny<LinkBasedActionConfig>()))
                 .ReturnsAsync(true);

            var response = await _sut.SaveLinkBasedActionConfigAsync(ConfigRequest());

            response.IsSuccess.Should().BeTrue();
            response.WasCreated.Should().BeFalse();
            response.ConfigId.Should().Be("config-1");
            response.Config!.ContextName.Should().Be("orders");
            response.Config.ShortUrlBase.Should().Be("https://s.example.com");
            response.Config.UpdatedAt.Should().NotBeNull();
            response.Config.CreatedAt.Should().Be(existing.CreatedAt, "an update must not restamp creation");
            _repo.Verify(r => r.CreateLinkBasedActionConfigAsync(It.IsAny<LinkBasedActionConfig>()), Times.Never);
        }

        [Fact]
        public async Task A_rejected_update_is_reported_as_a_failure()
        {
            _repo.Setup(r => r.GetLinkBasedActionConfigAsync(It.IsAny<string>()))
                 .ReturnsAsync(new LinkBasedActionConfig { ItemId = "config-1" });
            _repo.Setup(r => r.UpdateLinkBasedActionConfigAsync(It.IsAny<LinkBasedActionConfig>()))
                 .ReturnsAsync(false);

            var response = await _sut.SaveLinkBasedActionConfigAsync(ConfigRequest());

            response.IsSuccess.Should().BeFalse();
            response.ErrorMessage.Should().Be("Failed to update configuration");
        }

        [Fact]
        public async Task A_repository_failure_while_saving_is_reported_on_the_response()
        {
            _repo.Setup(r => r.GetLinkBasedActionConfigAsync(It.IsAny<string>()))
                 .ThrowsAsync(new InvalidOperationException("mongo unreachable"));

            var response = await _sut.SaveLinkBasedActionConfigAsync(ConfigRequest());

            response.IsSuccess.Should().BeFalse();
            response.ErrorMessage.Should().Contain("mongo unreachable");
        }

        #endregion
    }
}
