using System.Text.Json;
using Blocks.Genesis;
using Blocks.Secrets;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using WebPush;

namespace DomainService.Notification
{
    /// <summary>
    /// One VAPID keypair per tenant, stored as a platform secret named <see cref="SecretName"/>.
    /// Opens its own DI scope for the scoped <see cref="ISecretService"/> (same pattern as Office365TokenProvider).
    /// </summary>
    public class WebPushVapidKeyService : IWebPushVapidKeyService
    {
        public const string SecretName = "blocks-webpush-vapid";
        public const string DefaultSubject = "mailto:noreply@blocks.platform";

        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<WebPushVapidKeyService> _logger;

        public WebPushVapidKeyService(IServiceScopeFactory scopeFactory, ILogger<WebPushVapidKeyService> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        public async Task<string> GetOrCreatePublicKeyAsync(CancellationToken cancellationToken = default)
        {
            var material = await EnsureMaterialAsync(cancellationToken).ConfigureAwait(false);
            return material.PublicKey;
        }

        public async Task<(string PublicKey, string PrivateKey, string Subject)> GetVapidDetailsAsync(
            CancellationToken cancellationToken = default)
        {
            var material = await EnsureMaterialAsync(cancellationToken).ConfigureAwait(false);
            return (material.PublicKey, material.PrivateKey, material.Subject);
        }

        public async Task RotateAsync(CancellationToken cancellationToken = default)
        {
            await WithSecretsAsync(async secrets =>
            {
                var existing = await FindByNameAsync(secrets, cancellationToken).ConfigureAwait(false);
                var generated = VapidHelper.GenerateVapidKeys();
                var value = Serialize(new VapidMaterial
                {
                    PublicKey = generated.PublicKey,
                    PrivateKey = generated.PrivateKey,
                    Subject = DefaultSubject,
                });

                if (existing is null)
                {
                    await secrets.SetAsync(new SetSecretRequest
                    {
                        Name = SecretName,
                        Description = "Blocks Web Push VAPID keypair",
                        Type = SecretTypes.Service,
                        Value = value,
                        Tags = ["webpush", "vapid"],
                    }, cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    await secrets.RotateAsync(existing.SecretId, new RotateSecretRequest { Value = value }, cancellationToken)
                        .ConfigureAwait(false);
                }
            }).ConfigureAwait(false);
        }

        private async Task<VapidMaterial> EnsureMaterialAsync(CancellationToken cancellationToken)
        {
            return await WithSecretsAsync(async secrets =>
            {
                var existing = await FindByNameAsync(secrets, cancellationToken).ConfigureAwait(false);
                if (existing is not null)
                {
                    var raw = await secrets.GetValueAsync(existing.SecretId, cancellationToken).ConfigureAwait(false);
                    return Deserialize(raw);
                }

                var generated = VapidHelper.GenerateVapidKeys();
                var material = new VapidMaterial
                {
                    PublicKey = generated.PublicKey,
                    PrivateKey = generated.PrivateKey,
                    Subject = DefaultSubject,
                };
                await secrets.SetAsync(new SetSecretRequest
                {
                    Name = SecretName,
                    Description = "Blocks Web Push VAPID keypair",
                    Type = SecretTypes.Service,
                    Value = Serialize(material),
                    Tags = ["webpush", "vapid"],
                }, cancellationToken).ConfigureAwait(false);
                _logger.LogInformation("Created Web Push VAPID keypair secret {SecretName}", SecretName);
                return material;
            }).ConfigureAwait(false);
        }

        private static async Task<SecretResult?> FindByNameAsync(ISecretService secrets, CancellationToken cancellationToken)
        {
            var result = await secrets.FindAsync(new SecretFilter
            {
                Search = SecretName,
                PageNumber = 0,
                PageSize = 50,
            }, cancellationToken).ConfigureAwait(false);

            return (result.Data ?? Array.Empty<SecretResult>())
                .FirstOrDefault(s => string.Equals(s.Name, SecretName, StringComparison.OrdinalIgnoreCase));
        }

        private async Task<T> WithSecretsAsync<T>(Func<ISecretService, Task<T>> action)
        {
            using var scope = _scopeFactory.CreateScope();
            var secrets = scope.ServiceProvider.GetRequiredService<ISecretService>();
            return await action(secrets).ConfigureAwait(false);
        }

        private async Task WithSecretsAsync(Func<ISecretService, Task> action)
        {
            using var scope = _scopeFactory.CreateScope();
            var secrets = scope.ServiceProvider.GetRequiredService<ISecretService>();
            await action(secrets).ConfigureAwait(false);
        }

        private static string Serialize(VapidMaterial material) =>
            JsonSerializer.Serialize(material);

        private static VapidMaterial Deserialize(string raw)
        {
            var material = JsonSerializer.Deserialize<VapidMaterial>(raw)
                ?? throw new InvalidOperationException("Stored VAPID secret value is empty.");
            if (string.IsNullOrWhiteSpace(material.PublicKey) || string.IsNullOrWhiteSpace(material.PrivateKey))
                throw new InvalidOperationException("Stored VAPID secret is missing keys.");
            if (string.IsNullOrWhiteSpace(material.Subject))
                material.Subject = DefaultSubject;
            return material;
        }

        private sealed class VapidMaterial
        {
            public string PublicKey { get; set; } = string.Empty;
            public string PrivateKey { get; set; } = string.Empty;
            public string Subject { get; set; } = DefaultSubject;
        }
    }
}
