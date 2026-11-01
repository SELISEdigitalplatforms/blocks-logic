using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace DomainService.Notification.Push
{
    /// <summary>The fields of a Google service-account JSON key that push needs.</summary>
    public sealed record ServiceAccountKey(string ProjectId, string ClientEmail, string PrivateKeyPem)
    {
        public const int MaxJsonBytes = 20 * 1024;
        private const string ServiceAccountType = "service_account";

        /// <summary>
        /// Parses and checks a key: at most 20 KB, JSON with <c>type</c> = service_account, <c>project_id</c>,
        /// <c>client_email</c> and a <c>private_key</c> that loads as an RSA key. Errors name the field only and never
        /// hold any part of the key.
        /// </summary>
        public static ServiceAccountKey? TryParse(string? json, out string? error)
        {
            error = null;
            if (string.IsNullOrWhiteSpace(json))
            {
                error = "The service account key is empty.";
                return null;
            }

            if (Encoding.UTF8.GetByteCount(json) > MaxJsonBytes)
            {
                error = "The service account key is larger than 20 KB.";
                return null;
            }

            try
            {
                using var document = JsonDocument.Parse(json);
                return FromJson(document.RootElement, out error);
            }
            catch (JsonException)
            {
                error = "The service account key is not valid JSON.";
                return null;
            }
        }

        private static ServiceAccountKey? FromJson(JsonElement root, out string? error)
        {
            if (root.ValueKind != JsonValueKind.Object || Text(root, "type") != ServiceAccountType)
            {
                error = "The key must be a service account key (type service_account).";
                return null;
            }

            var projectId = Text(root, "project_id");
            var clientEmail = Text(root, "client_email");
            var privateKey = Text(root, "private_key");
            error = MissingField(projectId, clientEmail, privateKey);
            return error is null ? new ServiceAccountKey(projectId!, clientEmail!, privateKey!) : null;
        }

        private static string? MissingField(string? projectId, string? clientEmail, string? privateKey)
        {
            if (projectId is null)
                return "The key has no project_id.";
            if (clientEmail is null)
                return "The key has no client_email.";
            if (privateKey is null || !IsRsaKey(privateKey))
                return "The key has no valid private_key.";
            return null;
        }

        private static string? Text(JsonElement root, string name) =>
            root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString())
                ? value.GetString()!.Trim()
                : null;

        private static bool IsRsaKey(string pem)
        {
            try
            {
                using var rsa = RSA.Create();
                rsa.ImportFromPem(pem);
                return true;
            }
            catch (Exception ex) when (ex is ArgumentException or CryptographicException)
            {
                return false;
            }
        }
    }
}
