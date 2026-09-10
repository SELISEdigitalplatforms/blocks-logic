using Blocks.Secrets;
using FluentAssertions;
using Functions.DomainService.Services;
using Moq;

namespace XUnitTest.Functions
{
    /// <summary>
    /// The picker's catalog. The interesting case is the secret type: <c>SecretTypes</c> are the
    /// string constants "service", "api" and "both", and <see cref="SecretFilter.Type"/> holds one
    /// value — so a filter on "service" alone silently hides every secret the tenant marked
    /// "both", which is explicitly usable as a service secret. That gap arrived with
    /// SeliseBlocks.Secrets.OS 4.0.2, which added "both"; these tests pin the fix.
    /// </summary>
    public class FunctionSecretCatalogServiceTests
    {
        private readonly Mock<ISecretService> _secretService = new(MockBehavior.Strict);

        private static SecretResult Secret(string id, string name)
        {
            var secret = new SecretResult();
            secret.GetType().GetProperty(nameof(SecretResult.SecretId))!.SetValue(secret, id);
            secret.GetType().GetProperty(nameof(SecretResult.Name))!.SetValue(secret, name);
            return secret;
        }

        private void Returns(string type, params SecretResult[] secrets)
        {
            _secretService
                .Setup(s => s.FindAsync(It.Is<SecretFilter>(f => f.Type == type), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new SecretListResult { Data = secrets.ToList(), TotalCount = secrets.Length });
        }

        private FunctionSecretCatalogService Service() => new(_secretService.Object);

        [Fact]
        public async Task Lists_service_secrets()
        {
            Returns(SecretTypes.Service, Secret("s1", "STRIPE_KEY"));
            Returns(SecretTypes.Both);

            var entries = await Service().ListAsync();

            entries.Should().ContainSingle();
            entries[0].Id.Should().Be("s1");
            entries[0].Name.Should().Be("STRIPE_KEY");
        }

        [Fact]
        public async Task Includes_secrets_typed_both_not_only_service()
        {
            Returns(SecretTypes.Service, Secret("s1", "STRIPE_KEY"));
            Returns(SecretTypes.Both, Secret("s2", "SHARED_TOKEN"));

            var entries = await Service().ListAsync();

            entries.Select(e => e.Id).Should().BeEquivalentTo(["s1", "s2"]);
        }

        [Fact]
        public async Task A_secret_returned_by_both_queries_is_listed_once()
        {
            // Whether the store folds "both" into a "service" query is its business; merging by
            // id means this service is correct either way.
            var shared = Secret("s1", "STRIPE_KEY");
            Returns(SecretTypes.Service, shared);
            Returns(SecretTypes.Both, shared);

            var entries = await Service().ListAsync();

            entries.Should().ContainSingle().Which.Id.Should().Be("s1");
        }

        [Fact]
        public async Task Never_exposes_a_value_only_id_and_name()
        {
            Returns(SecretTypes.Service, Secret("s1", "STRIPE_KEY"));
            Returns(SecretTypes.Both);

            var entries = await Service().ListAsync();

            // SecretCatalogEntry is a two-field record by construction; this asserts the shape
            // stays that way, because the picker must never carry a secret's value to a browser.
            typeof(SecretCatalogEntry).GetProperties().Select(p => p.Name)
                .Should().BeEquivalentTo(["Id", "Name"]);
            entries.Should().ContainSingle();
        }

        [Fact]
        public async Task Sorts_by_name_so_the_picker_order_is_stable()
        {
            Returns(SecretTypes.Service, Secret("s2", "zeta"), Secret("s1", "alpha"));
            Returns(SecretTypes.Both, Secret("s3", "Mid"));

            var entries = await Service().ListAsync();

            entries.Select(e => e.Name).Should().ContainInOrder("alpha", "Mid", "zeta");
        }
    }
}
