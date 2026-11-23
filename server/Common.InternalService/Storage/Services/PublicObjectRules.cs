using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Common.InternalService.Storage
{
    /// <summary>
    /// The provider-neutral rules for public objects: what a container name and a key may look like, the S3 bucket
    /// policy that makes a bucket's objects readable without credentials, and the permanent URL of an object.
    /// Pure functions, so every provider and the tests share one definition. Kept in step with the same class in
    /// blocks-data's Storage.DomainService, which blocks-logic does not reference (it has its own IStorageService).
    /// </summary>
    public static partial class PublicObjectRules
    {
        private const int MaxKeyBytes = 1024;

        public const string PublicReadStatementId = "BlocksPublicReadObjects";

        /// <summary>Lowercase letters, digits and hyphens, 3 to 63 characters: valid as an Azure container and as an S3 bucket.</summary>
        [GeneratedRegex("^[a-z0-9][a-z0-9-]{1,61}[a-z0-9]$", RegexOptions.CultureInvariant)]
        private static partial Regex ContainerNamePattern();

        /// <summary>Throws <see cref="PublicObjectStorageException"/> when the container or the key could address something other than one object.</summary>
        public static void Validate(string container, string key)
        {
            if (string.IsNullOrEmpty(container) || !ContainerNamePattern().IsMatch(container))
            {
                throw new PublicObjectStorageException(PublicObjectStorageException.InvalidContainerCode,
                    "A container name is 3 to 63 lowercase letters, digits or hyphens.");
            }

            if (string.IsNullOrWhiteSpace(key)
                || Encoding.UTF8.GetByteCount(key) > MaxKeyBytes
                || key.Any(char.IsControl)
                || key.Split('/').Any(segment => segment.Length == 0 || segment is "." or ".."))
            {
                throw new PublicObjectStorageException(PublicObjectStorageException.InvalidKeyCode,
                    "An object key is 1 to 1024 bytes, with no empty, '.' or '..' path segment and no control character.");
            }
        }

        /// <summary>The ARN of every object in a bucket: <c>arn:aws:s3:::bucket/*</c>.</summary>
        public static string ObjectsArn(string bucket) => $"arn:aws:s3:::{bucket}/*";

        /// <summary>
        /// The policy for a bucket that holds only public objects: anyone may read an object whose exact key they know
        /// (<c>s3:GetObject</c>). Listing the bucket, writing and deleting are not granted.
        /// </summary>
        public static string BuildPublicReadPolicy(string bucket) => AddPublicReadStatement(null, bucket)!;

        /// <summary>
        /// Adds the public-read statement to a bucket's existing policy document and keeps every other statement.
        /// Returns null when the policy already allows anonymous <c>s3:GetObject</c> on the bucket's objects, so the
        /// caller has nothing to write.
        /// </summary>
        public static string? AddPublicReadStatement(string? existingPolicy, string bucket)
        {
            JsonObject document;
            if (string.IsNullOrWhiteSpace(existingPolicy))
            {
                document = new JsonObject
                {
                    ["Version"] = "2012-10-17",
                    ["Statement"] = new JsonArray()
                };
            }
            else
            {
                document = JsonNode.Parse(existingPolicy) as JsonObject
                    ?? throw new InvalidOperationException("The bucket policy is not a JSON object.");

                if (document["Statement"] is JsonObject single)
                {
                    document["Statement"] = new JsonArray(single.DeepClone());
                }

                document["Statement"] ??= new JsonArray();
            }

            var statements = document["Statement"] as JsonArray
                ?? throw new InvalidOperationException("The bucket policy has no statement list.");

            if (statements.OfType<JsonObject>().Any(s => AllowsAnonymousRead(s, bucket)))
            {
                return null;
            }

            statements.Add(new JsonObject
            {
                ["Sid"] = PublicReadStatementId,
                ["Effect"] = "Allow",
                ["Principal"] = "*",
                ["Action"] = new JsonArray("s3:GetObject"),
                ["Resource"] = new JsonArray(ObjectsArn(bucket))
            });

            return document.ToJsonString();
        }

        private static bool AllowsAnonymousRead(JsonObject statement, string bucket)
        {
            if (!string.Equals(statement["Effect"]?.GetValue<string>(), "Allow", StringComparison.Ordinal)
                || statement["Condition"] is not null)
            {
                return false;
            }

            var principalIsAnyone = statement["Principal"] switch
            {
                JsonValue value => AnyValueMatches(value, v => v == "*"),
                JsonObject obj => AnyValueMatches(obj["AWS"], v => v == "*"),
                _ => false
            };

            return principalIsAnyone
                && AnyValueMatches(statement["Action"], a => a is "s3:GetObject" or "s3:*" or "*")
                && AnyValueMatches(statement["Resource"], r => r == ObjectsArn(bucket) || r == "*");
        }

        private static bool AnyValueMatches(JsonNode? node, Func<string, bool> predicate) => node switch
        {
            JsonValue value => value.TryGetValue<string>(out var text) && predicate(text),
            JsonArray array => array.Any(item => AnyValueMatches(item, predicate)),
            _ => false
        };

        /// <summary>
        /// The permanent address of an object: <c>{endpoint}/{bucket}/{key}</c> for a store with its own endpoint
        /// (path style), or <c>https://{bucket}.{s3 host}/{key}</c> for AWS. Keys are escaped per path segment.
        /// </summary>
        /// <param name="serviceUrl">The store's own endpoint, or null for AWS.</param>
        /// <param name="awsS3Host">The regional S3 host, for example <c>s3.eu-central-1.amazonaws.com</c>; used when there is no endpoint.</param>
        public static string BuildS3ObjectUrl(string? serviceUrl, string? awsS3Host, string bucket, string key)
        {
            var escapedKey = string.Join('/', key.Split('/').Select(Uri.EscapeDataString));

            if (!string.IsNullOrWhiteSpace(serviceUrl))
            {
                return $"{serviceUrl.TrimEnd('/')}/{bucket}/{escapedKey}";
            }

            if (string.IsNullOrWhiteSpace(awsS3Host))
            {
                throw new PublicObjectStorageException(PublicObjectStorageException.FailedCode,
                    "The S3 client has neither an endpoint nor a region, so the object's address is unknown.");
            }

            return $"https://{bucket}.{awsS3Host}/{escapedKey}";
        }
    }
}
