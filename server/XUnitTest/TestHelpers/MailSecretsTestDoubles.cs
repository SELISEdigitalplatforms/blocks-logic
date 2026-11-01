using Blocks.Genesis;
using Blocks.Secrets;
using Mail.DomainService.Mails;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace XUnitTest.TestHelpers
{
    /// <summary>Shared doubles for Blocks Secrets reads of mail passwords and connection strings (#371).</summary>
    internal static class MailSecretsTestDoubles
    {
        /// <summary>
        /// A scope factory whose scopes serve <paramref name="secrets"/> as the scoped ISecretService,
        /// under scope validation. The mock doubles as the counting fake: verify GetValueAsync on it.
        /// Without one, a strict mock fails any read, which suits callers that only use legacy records.
        /// </summary>
        public static IServiceScopeFactory ScopeFactory(Mock<ISecretService>? secrets = null)
        {
            var secretService = (secrets ?? new Mock<ISecretService>(MockBehavior.Strict)).Object;
            var services = new ServiceCollection();
            services.AddScoped(_ => secretService);
            var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });

            return provider.GetRequiredService<IServiceScopeFactory>();
        }

        /// <summary>A real <see cref="MailPasswordResolver"/> over <see cref="ScopeFactory"/>.</summary>
        public static MailPasswordResolver PasswordResolver(Mock<ISecretService>? secrets = null) =>
            new(ScopeFactory(secrets));

        /// <summary>A secret service that answers <paramref name="value"/> for <paramref name="reference"/> and records the context of each read.</summary>
        public static Mock<ISecretService> Answering(string reference, string value, List<BlocksContext?>? seen = null)
        {
            var secrets = new Mock<ISecretService>(MockBehavior.Strict);
            secrets.Setup(s => s.GetValueAsync(reference, It.IsAny<CancellationToken>()))
                   .Callback(() => seen?.Add(BlocksContext.GetContext()))
                   .ReturnsAsync(value);
            return secrets;
        }

        /// <summary>A secret service whose read of <paramref name="reference"/> throws <paramref name="exception"/>.</summary>
        public static Mock<ISecretService> Throwing(string reference, Exception exception)
        {
            var secrets = new Mock<ISecretService>(MockBehavior.Strict);
            secrets.Setup(s => s.GetValueAsync(reference, It.IsAny<CancellationToken>())).ThrowsAsync(exception);
            return secrets;
        }

        /// <summary>One instance of each Secret* failure the spec's table names, plus the ones mapped to <c>unexpected</c>.</summary>
        public static Exception SecretFailure(string name, string reference) => name switch
        {
            nameof(SecretNotFoundException) => new SecretNotFoundException(reference),
            nameof(SecretStateException) => new SecretStateException($"Secret '{reference}' is archived", "read"),
            nameof(SecretAccessDeniedException) => new SecretAccessDeniedException($"denied for '{reference}'"),
            nameof(SecretVaultException) => new SecretVaultException("vault down", "get", reference),
            nameof(SecretValidationException) => new SecretValidationException($"bad id '{reference}'"),
            nameof(TimeoutException) => new TimeoutException($"slow '{reference}'"),
            _ => new InvalidOperationException($"misconfigured for '{reference}'"),
        };

        public static int Reads(this Mock<ISecretService> secrets) =>
            secrets.Invocations.Count(i => i.Method.Name == nameof(ISecretService.GetValueAsync));
    }
}
