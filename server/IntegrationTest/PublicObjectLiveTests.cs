using System.Net;
using System.Text;
using Amazon.S3;
using Amazon.S3.Model;
using Azure.Storage.Blobs;
using Blocks.Genesis;
using Blocks.Genesis.Entities.Data;
using Common.InternalService.Storage;
using FluentAssertions;

namespace IntegrationTest;

/// <summary>
/// The public-object methods of blocks-logic's own storage services against real stores (I02-WP9): Azurite (or an Azure account) and an S3-compatible
/// store such as the toolchain's SeaweedFS. A certificate uploads, its URL reads without credentials, and nothing
/// else becomes readable: not the tenant's own private containers or buckets, not a listing of the public bucket,
/// and no anonymous write or delete.
///
/// <para>
/// A separate project, so the unit run (XUnitTest) stays fast and needs no store. Each test reads its endpoint from these
/// variables and FAILS, naming the missing one, when it is not set. Nothing here ever prints a key or a connection string.
/// <list type="bullet">
/// <item><c>TEST_AZURITE_CONNECTION_STRING</c></item>
/// <item><c>TEST_S3_ENDPOINT</c>, <c>TEST_S3_ACCESS_KEY</c>, <c>TEST_S3_SECRET_KEY</c></item>
/// </list>
/// The endpoints must be reachable by this test process (the same name the stored URL carries, because the test
/// reads that URL back).
/// </para>
/// </summary>
[Trait("Category", "LiveStorage")]
public sealed class PublicObjectLiveTests : IDisposable
{
    private const string PublicContainer = "certificates";
    private static readonly byte[] CertificateBytes = Encoding.ASCII.GetBytes("-----BEGIN CERTIFICATE-----\nlive-test\n-----END CERTIFICATE-----\n");

    private readonly string _tenant = "wp9" + Guid.NewGuid().ToString("N")[..10];
    private readonly HttpClient _anonymous = new() { Timeout = TimeSpan.FromSeconds(30) };
    private readonly List<Func<Task>> _cleanup = new();

    public PublicObjectLiveTests() => SetTenant(_tenant);

    public void Dispose()
    {
        foreach (var step in _cleanup)
        {
            try { step().GetAwaiter().GetResult(); }
            catch (Exception ex) when (ex is AmazonS3Exception or Azure.RequestFailedException or HttpRequestException) { /* best effort */ }
        }

        _anonymous.Dispose();
        BlocksContext.SetContext(null);
    }

    private static void SetTenant(string tenantId) =>
        BlocksContext.SetContext(BlocksContext.Create(
            tenantId: tenantId,
            roles: new[] { "User" },
            userId: "user-1",
            isAuthenticated: true,
            requestUri: "/",
            organizationId: "org-1",
            expireOn: DateTime.UtcNow.AddHours(1),
            email: "user@example.com",
            permissions: Array.Empty<string>(),
            userName: "user",
            phoneNumber: "",
            displayName: "User",
            oauthToken: "",
            originalTenantId: tenantId));

    private static string Required(string name) =>
        Environment.GetEnvironmentVariable(name) is { Length: > 0 } value
            ? value
            : throw new Xunit.Sdk.XunitException($"{name} is not set. This test needs a live store (category LiveStorage); see the class summary.");

    // Azure

    private AzureBlobStorageService AzureService(out BlobServiceClient admin)
    {
        var connectionString = Required("TEST_AZURITE_CONNECTION_STRING");
        admin = new BlobServiceClient(connectionString);
        return new AzureBlobStorageService(
            new StorageConfiguration { StorageStrategy = "azure", ConnectionString = connectionString });
    }

    [Fact]
    public async Task Azure_a_certificate_uploads_reads_anonymously_and_nothing_else_becomes_readable()
    {
        var sut = AzureService(out var admin);
        var key = $"{_tenant}_3rdparty.pfx";
        var quarantine = admin.GetBlobContainerClient(_tenant + "-quarantine");
        // The service constructor created the tenant container; the private one is created below.
        _cleanup.Add(async () => await admin.GetBlobContainerClient(PublicContainer).DeleteBlobIfExistsAsync(key));
        _cleanup.Add(async () => await admin.DeleteBlobContainerAsync(_tenant));
        _cleanup.Add(async () => await admin.DeleteBlobContainerAsync(_tenant + "-quarantine"));

        using var content = new MemoryStream(CertificateBytes);
        var url = await sut.UploadPublicObjectAsync(PublicContainer, key, content, "application/x-pkcs12");
        url.Should().EndWith($"/{PublicContainer}/{key}");
        url.Should().NotContain("?");

        var read = await _anonymous.GetAsync(url);
        read.StatusCode.Should().Be(HttpStatusCode.OK);
        (await read.Content.ReadAsByteArrayAsync()).Should().Equal(CertificateBytes);
        read.Content.Headers.ContentType?.MediaType.Should().Be("application/x-pkcs12");

        // Other objects stay private: a blob in the private quarantine container is refused without credentials.
        await quarantine.CreateIfNotExistsAsync(Azure.Storage.Blobs.Models.PublicAccessType.None);
        await quarantine.GetBlobClient("private-object.txt").UploadAsync(new MemoryStream(Encoding.ASCII.GetBytes("secret")), overwrite: true);
        var privateUrl = url.Replace($"/{PublicContainer}/{key}", $"/{_tenant}-quarantine/private-object.txt");
        (await _anonymous.GetAsync(privateUrl)).StatusCode.Should().NotBe(HttpStatusCode.OK);

        // "Blob" access serves a blob whose URL is known; it does not list the container, and it does not allow writes or deletes.
        var containerUrl = url[..url.LastIndexOf($"/{key}", StringComparison.Ordinal)];
        (await _anonymous.GetAsync(containerUrl + "?restype=container&comp=list")).StatusCode.Should().NotBe(HttpStatusCode.OK);
        var anonymousWrite = new HttpRequestMessage(HttpMethod.Put, containerUrl + "/anonymous-write.txt") { Content = new StringContent("x") };
        anonymousWrite.Headers.Add("x-ms-blob-type", "BlockBlob");
        (await _anonymous.SendAsync(anonymousWrite)).IsSuccessStatusCode.Should().BeFalse();
        (await _anonymous.DeleteAsync(url)).IsSuccessStatusCode.Should().BeFalse();
        (await _anonymous.GetAsync(url)).StatusCode.Should().Be(HttpStatusCode.OK, "the failed anonymous delete must not have removed the certificate");

        await using (var stream = await sut.OpenReadAsync(PublicContainer, key))
        {
            using var copy = new MemoryStream();
            await stream!.CopyToAsync(copy);
            copy.ToArray().Should().Equal(CertificateBytes);
        }
        (await sut.OpenReadAsync(PublicContainer, "no-such-key-" + _tenant)).Should().BeNull();

        var replacement = Encoding.ASCII.GetBytes("replacement");
        await sut.UploadPublicObjectAsync(PublicContainer, key, new MemoryStream(replacement), "application/x-pem-file");
        (await _anonymous.GetByteArrayAsync(url)).Should().Equal(replacement);
    }

    [Fact]
    public async Task Azure_an_existing_private_container_is_made_readable_by_the_upload()
    {
        var sut = AzureService(out var admin);
        var container = _tenant + "-pub";
        await admin.GetBlobContainerClient(container).CreateIfNotExistsAsync(Azure.Storage.Blobs.Models.PublicAccessType.None);
        _cleanup.Add(async () => await admin.DeleteBlobContainerAsync(container));
        _cleanup.Add(async () => await admin.DeleteBlobContainerAsync(_tenant));
        _cleanup.Add(async () => await admin.DeleteBlobContainerAsync(_tenant + "-quarantine"));

        var url = await sut.UploadPublicObjectAsync(container, "a.crt", new MemoryStream(CertificateBytes), "application/x-x509-ca-cert");

        (await _anonymous.GetAsync(url)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // S3

    private AwsS3CompatibleStorageService S3Service(out AmazonS3Client admin)
    {
        var endpoint = Required("TEST_S3_ENDPOINT");
        var accessKey = Required("TEST_S3_ACCESS_KEY");
        var secretKey = Required("TEST_S3_SECRET_KEY");

        admin = new AmazonS3Client(accessKey, secretKey, new AmazonS3Config { ServiceURL = endpoint, ForcePathStyle = true });
        return new AwsS3CompatibleStorageService(new StorageConfiguration
        {
            StorageStrategy = "s3compatible",
            Host = endpoint,
            AccessKey = accessKey,
            SecretKey = secretKey
        });
    }

    [Fact]
    public async Task S3_a_certificate_uploads_reads_anonymously_and_nothing_else_becomes_readable()
    {
        var sut = S3Service(out var admin);
        var key = $"{_tenant}_3rdparty.pfx";
        var privateKey = "private-object.txt";
        // The service's constructor created the tenant's private bucket and its quarantine bucket.
        _cleanup.Add(async () => await admin.DeleteObjectAsync(PublicContainer, key));
        _cleanup.Add(async () => { await admin.DeleteObjectAsync(_tenant, privateKey); await admin.DeleteBucketAsync(_tenant); });
        _cleanup.Add(async () => await admin.DeleteBucketAsync(_tenant + "-quarantine"));

        // Upload: the URL is permanent (no signature) and points at the one public bucket.
        using var content = new MemoryStream(CertificateBytes);
        var url = await sut.UploadPublicObjectAsync(PublicContainer, key, content, "application/x-pkcs12");
        url.Should().EndWith($"/{PublicContainer}/{key}");
        url.Should().NotContain("?");

        // Anonymous read of the certificate works and serves the bytes and the content type.
        var read = await _anonymous.GetAsync(url);
        read.StatusCode.Should().Be(HttpStatusCode.OK);
        (await read.Content.ReadAsByteArrayAsync()).Should().Equal(CertificateBytes);
        read.Content.Headers.ContentType?.MediaType.Should().Be("application/x-pkcs12");

        // Other objects stay private: an object in the tenant's own bucket is refused without credentials.
        await admin.PutObjectAsync(new PutObjectRequest { BucketName = _tenant, Key = privateKey, ContentBody = "secret" });
        var privateUrl = url.Replace($"/{PublicContainer}/{key}", $"/{_tenant}/{privateKey}");
        (await _anonymous.GetAsync(privateUrl)).StatusCode.Should().NotBe(HttpStatusCode.OK);

        // The public bucket cannot be listed, written or deleted without credentials.
        var bucketUrl = url[..url.LastIndexOf($"/{key}", StringComparison.Ordinal)];
        (await _anonymous.GetAsync(bucketUrl + "/")).StatusCode.Should().NotBe(HttpStatusCode.OK);
        (await _anonymous.PutAsync(bucketUrl + "/anonymous-write.txt", new StringContent("x"))).IsSuccessStatusCode.Should().BeFalse();
        (await _anonymous.DeleteAsync(url)).IsSuccessStatusCode.Should().BeFalse();
        (await _anonymous.GetAsync(url)).StatusCode.Should().Be(HttpStatusCode.OK, "the failed anonymous delete must not have removed the certificate");

        // The service reads it back with its own credentials, and a missing key is null.
        await using (var stream = await sut.OpenReadAsync(PublicContainer, key))
        {
            using var copy = new MemoryStream();
            await stream!.CopyToAsync(copy);
            copy.ToArray().Should().Equal(CertificateBytes);
        }
        (await sut.OpenReadAsync(PublicContainer, "no-such-key-" + _tenant)).Should().BeNull();

        // A second upload to the same key replaces the certificate in place.
        var replacement = Encoding.ASCII.GetBytes("replacement");
        await sut.UploadPublicObjectAsync(PublicContainer, key, new MemoryStream(replacement), "application/x-pem-file");
        (await _anonymous.GetByteArrayAsync(url)).Should().Equal(replacement);
    }
}
