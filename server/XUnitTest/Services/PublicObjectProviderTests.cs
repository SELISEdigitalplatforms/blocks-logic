using System.Net;
using System.Text;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Blocks.Genesis.Entities.Data;
using Common.InternalService.Storage;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Moq;

namespace XUnitTest.Services;

/// <summary>
/// <see cref="IStorageService.UploadPublicObjectAsync"/> and <see cref="IStorageService.OpenReadAsync"/> of blocks-logic's own
/// storage services, for each provider (I02-WP9). The provider SDK clients are mocked, so each call is counted exactly; the
/// real stores are covered by the IntegrationTest project (<c>PublicObjectLiveTests</c>).
/// </summary>
public class PublicObjectProviderTests
{
    // Azure

    private sealed class AzureUnderTest(BlobServiceClient service, BlobContainerClient own)
        : AzureBlobStorageService(service, own);

    private sealed class AzureFixture
    {
        public Mock<BlobServiceClient> Service { get; } = new();
        public Mock<BlobContainerClient> Container { get; } = new();
        public Mock<BlobClient> Blob { get; } = new();
        public BlobUploadOptions? Uploaded { get; private set; }

        public AzureFixture(Azure.Response<BlobContainerInfo>? createResponse, PublicAccessType? existingAccess = null)
        {
            Service.Setup(s => s.GetBlobContainerClient("certificates")).Returns(Container.Object);
            Container.Setup(c => c.CreateIfNotExistsAsync(It.IsAny<PublicAccessType>(), It.IsAny<IDictionary<string, string>>(), It.IsAny<BlobContainerEncryptionScopeOptions>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(createResponse!);
            Container.Setup(c => c.GetBlobClient("tenant-1")).Returns(Blob.Object);
            Blob.SetupGet(b => b.Uri).Returns(new Uri("http://azurite:10000/devstoreaccount1/certificates/tenant-1"));
            Blob.Setup(b => b.UploadAsync(It.IsAny<Stream>(), It.IsAny<BlobUploadOptions>(), It.IsAny<CancellationToken>()))
                .Callback<Stream, BlobUploadOptions, CancellationToken>((_, options, _) => Uploaded = options)
                .ReturnsAsync(Mock.Of<Azure.Response<BlobContentInfo>>());

            if (existingAccess is not null)
            {
                Container.Setup(c => c.GetAccessPolicyAsync(It.IsAny<BlobRequestConditions>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync(Azure.Response.FromValue(BlobsModelFactory.BlobContainerAccessPolicy(blobPublicAccess: existingAccess.Value, eTag: default, lastModified: default, signedIdentifiers: Array.Empty<BlobSignedIdentifier>()), Mock.Of<Azure.Response>()));
                Container.Setup(c => c.SetAccessPolicyAsync(It.IsAny<PublicAccessType>(), It.IsAny<IEnumerable<BlobSignedIdentifier>>(), It.IsAny<BlobRequestConditions>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync(Mock.Of<Azure.Response<BlobContainerInfo>>());
            }
        }

        public AzureUnderTest Sut() => new(Service.Object, Mock.Of<BlobContainerClient>());
    }

    private static Azure.Response<BlobContainerInfo> Created() => Mock.Of<Azure.Response<BlobContainerInfo>>();

    [Fact]
    public async Task Azure_creates_the_container_with_blob_access_and_returns_the_blobs_own_url()
    {
        var fixture = new AzureFixture(Created());
        using var content = new MemoryStream(new byte[] { 1, 2, 3 });

        var url = await fixture.Sut().UploadPublicObjectAsync("certificates", "tenant-1", content, "application/x-pkcs12");

        url.Should().Be("http://azurite:10000/devstoreaccount1/certificates/tenant-1");
        url.Should().NotContain("?");
        fixture.Container.Verify(c => c.CreateIfNotExistsAsync(PublicAccessType.Blob, It.IsAny<IDictionary<string, string>>(), It.IsAny<BlobContainerEncryptionScopeOptions>(), It.IsAny<CancellationToken>()), Times.Once);
        fixture.Container.Verify(c => c.SetAccessPolicyAsync(It.IsAny<PublicAccessType>(), It.IsAny<IEnumerable<BlobSignedIdentifier>>(), It.IsAny<BlobRequestConditions>(), It.IsAny<CancellationToken>()), Times.Never);
        fixture.Uploaded!.HttpHeaders.ContentType.Should().Be("application/x-pkcs12");
    }

    [Fact]
    public async Task Azure_raises_an_existing_private_container_to_blob_access()
    {
        var fixture = new AzureFixture(createResponse: null, existingAccess: PublicAccessType.None);

        await fixture.Sut().UploadPublicObjectAsync("certificates", "tenant-1", new MemoryStream(), "text/plain");

        fixture.Container.Verify(c => c.SetAccessPolicyAsync(PublicAccessType.Blob, It.IsAny<IEnumerable<BlobSignedIdentifier>>(), It.IsAny<BlobRequestConditions>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Azure_leaves_an_existing_container_that_already_has_blob_access_alone()
    {
        var fixture = new AzureFixture(createResponse: null, existingAccess: PublicAccessType.Blob);

        await fixture.Sut().UploadPublicObjectAsync("certificates", "tenant-1", new MemoryStream(), "text/plain");

        fixture.Container.Verify(c => c.SetAccessPolicyAsync(It.IsAny<PublicAccessType>(), It.IsAny<IEnumerable<BlobSignedIdentifier>>(), It.IsAny<BlobRequestConditions>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Azure_open_read_returns_null_for_a_missing_blob_and_rethrows_other_failures()
    {
        var fixture = new AzureFixture(Created());
        fixture.Container.Setup(c => c.GetBlobClient("gone")).Returns(Mock.Of<BlobClient>(b =>
            b.DownloadStreamingAsync(It.IsAny<BlobDownloadOptions>(), It.IsAny<CancellationToken>()) == Task.FromException<Response<BlobDownloadStreamingResult>>(new RequestFailedException(404, "no", "BlobNotFound", null))));
        fixture.Container.Setup(c => c.GetBlobClient("denied")).Returns(Mock.Of<BlobClient>(b =>
            b.DownloadStreamingAsync(It.IsAny<BlobDownloadOptions>(), It.IsAny<CancellationToken>()) == Task.FromException<Response<BlobDownloadStreamingResult>>(new RequestFailedException(403, "no", "AuthorizationFailure", null))));

        (await fixture.Sut().OpenReadAsync("certificates", "gone")).Should().BeNull();
        Func<Task> act = () => fixture.Sut().OpenReadAsync("certificates", "denied");
        await act.Should().ThrowAsync<RequestFailedException>();
    }

    [Fact]
    public async Task Azure_open_read_returns_the_blobs_content()
    {
        var fixture = new AzureFixture(Created());
        var result = BlobsModelFactory.BlobDownloadStreamingResult(new MemoryStream(Encoding.UTF8.GetBytes("pem")));
        fixture.Container.Setup(c => c.GetBlobClient("tenant-1")).Returns(Mock.Of<BlobClient>(b =>
            b.DownloadStreamingAsync(It.IsAny<BlobDownloadOptions>(), It.IsAny<CancellationToken>()) == Task.FromResult(Azure.Response.FromValue(result, Mock.Of<Azure.Response>()))));

        await using var stream = await fixture.Sut().OpenReadAsync("certificates", "tenant-1");

        new StreamReader(stream!).ReadToEnd().Should().Be("pem");
    }

    // S3

    private sealed class S3UnderTest(AmazonS3Client client) : AwsS3StorageService(client);

    private static Mock<AmazonS3Client> S3Client(string? serviceUrl = "http://s3.test:8333")
    {
        var config = serviceUrl is null
            ? new AmazonS3Config { RegionEndpoint = Amazon.RegionEndpoint.EUCentral1 }
            : new AmazonS3Config { ServiceURL = serviceUrl, ForcePathStyle = true };
        return new Mock<AmazonS3Client>(new AnonymousAWSCredentials(), config);
    }

    private static AmazonS3Exception S3Error(HttpStatusCode status, string code) =>
        new("failure", ErrorType.Sender, code, "request-1", status);

    private static void GivenBucketExists(Mock<AmazonS3Client> client, string bucket) =>
        client.Setup(c => c.GetBucketLocationAsync(bucket, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetBucketLocationResponse());

    [Fact]
    public async Task S3_creates_a_missing_bucket_sets_the_policy_and_puts_the_object()
    {
        var client = S3Client();
        client.Setup(c => c.GetBucketLocationAsync("certificates", It.IsAny<CancellationToken>()))
            .ThrowsAsync(S3Error(HttpStatusCode.NotFound, "NoSuchBucket"));
        client.Setup(c => c.PutBucketAsync(It.IsAny<PutBucketRequest>(), It.IsAny<CancellationToken>())).ReturnsAsync(new PutBucketResponse());
        client.Setup(c => c.GetBucketPolicyAsync(It.IsAny<GetBucketPolicyRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(S3Error(HttpStatusCode.NotFound, "NoSuchBucketPolicy"));
        PutBucketPolicyRequest? policyRequest = null;
        client.Setup(c => c.PutBucketPolicyAsync(It.IsAny<PutBucketPolicyRequest>(), It.IsAny<CancellationToken>()))
            .Callback<PutBucketPolicyRequest, CancellationToken>((r, _) => policyRequest = r)
            .ReturnsAsync(new PutBucketPolicyResponse());
        PutObjectRequest? putRequest = null;
        client.Setup(c => c.PutObjectAsync(It.IsAny<PutObjectRequest>(), It.IsAny<CancellationToken>()))
            .Callback<PutObjectRequest, CancellationToken>((r, _) => putRequest = r)
            .ReturnsAsync(new PutObjectResponse());
        using var content = new MemoryStream(new byte[] { 1 });

        var url = await new S3UnderTest(client.Object).UploadPublicObjectAsync("certificates", "tenant-1", content, "application/x-pkcs12");

        url.Should().Be("http://s3.test:8333/certificates/tenant-1");
        client.Verify(c => c.PutBucketAsync(It.Is<PutBucketRequest>(r => r.BucketName == "certificates"), It.IsAny<CancellationToken>()), Times.Once);
        policyRequest!.BucketName.Should().Be("certificates");
        policyRequest.Policy.Should().Be(PublicObjectRules.BuildPublicReadPolicy("certificates"));
        putRequest!.BucketName.Should().Be("certificates");
        putRequest.Key.Should().Be("tenant-1");
        putRequest.ContentType.Should().Be("application/x-pkcs12");
        putRequest.InputStream.Should().BeSameAs(content);
    }

    [Fact]
    public async Task S3_writes_no_policy_when_the_bucket_already_allows_anonymous_reads()
    {
        var client = S3Client();
        GivenBucketExists(client, "certificates");
        client.Setup(c => c.GetBucketPolicyAsync(It.IsAny<GetBucketPolicyRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetBucketPolicyResponse { Policy = PublicObjectRules.BuildPublicReadPolicy("certificates") });
        client.Setup(c => c.PutObjectAsync(It.IsAny<PutObjectRequest>(), It.IsAny<CancellationToken>())).ReturnsAsync(new PutObjectResponse());

        await new S3UnderTest(client.Object).UploadPublicObjectAsync("certificates", "tenant-1", new MemoryStream(), "text/plain");

        client.Verify(c => c.PutBucketPolicyAsync(It.IsAny<PutBucketPolicyRequest>(), It.IsAny<CancellationToken>()), Times.Never);
        client.Verify(c => c.PutBucketAsync(It.IsAny<PutBucketRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task S3_touches_only_the_named_bucket()
    {
        var client = S3Client();
        GivenBucketExists(client, "certificates");
        client.Setup(c => c.GetBucketPolicyAsync(It.IsAny<GetBucketPolicyRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(S3Error(HttpStatusCode.NotFound, "NoSuchBucketPolicy"));
        client.Setup(c => c.PutBucketPolicyAsync(It.IsAny<PutBucketPolicyRequest>(), It.IsAny<CancellationToken>())).ReturnsAsync(new PutBucketPolicyResponse());
        client.Setup(c => c.PutObjectAsync(It.IsAny<PutObjectRequest>(), It.IsAny<CancellationToken>())).ReturnsAsync(new PutObjectResponse());

        await new S3UnderTest(client.Object).UploadPublicObjectAsync("certificates", "k", new MemoryStream(), "text/plain");

        client.Verify(c => c.PutBucketPolicyAsync(It.Is<PutBucketPolicyRequest>(r => r.BucketName != "certificates"), It.IsAny<CancellationToken>()), Times.Never);
        client.Verify(c => c.PutObjectAsync(It.Is<PutObjectRequest>(r => r.BucketName != "certificates"), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task S3_on_AWS_returns_the_virtual_hosted_regional_url()
    {
        var client = S3Client(serviceUrl: null);
        GivenBucketExists(client, "certificates");
        client.Setup(c => c.GetBucketPolicyAsync(It.IsAny<GetBucketPolicyRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetBucketPolicyResponse { Policy = PublicObjectRules.BuildPublicReadPolicy("certificates") });
        client.Setup(c => c.PutObjectAsync(It.IsAny<PutObjectRequest>(), It.IsAny<CancellationToken>())).ReturnsAsync(new PutObjectResponse());

        var url = await new S3UnderTest(client.Object).UploadPublicObjectAsync("certificates", "tenant-1", new MemoryStream(), "text/plain");

        url.Should().Be("https://certificates.s3.eu-central-1.amazonaws.com/tenant-1");
    }

    [Fact]
    public async Task S3_open_read_returns_null_for_a_missing_key_and_rethrows_other_failures()
    {
        var client = S3Client();
        client.Setup(c => c.GetObjectAsync(It.Is<GetObjectRequest>(r => r.Key == "gone"), It.IsAny<CancellationToken>()))
            .ThrowsAsync(S3Error(HttpStatusCode.NotFound, "NoSuchKey"));
        client.Setup(c => c.GetObjectAsync(It.Is<GetObjectRequest>(r => r.Key == "denied"), It.IsAny<CancellationToken>()))
            .ThrowsAsync(S3Error(HttpStatusCode.Forbidden, "AccessDenied"));
        var sut = new S3UnderTest(client.Object);

        (await sut.OpenReadAsync("certificates", "gone")).Should().BeNull();
        Func<Task> act = () => sut.OpenReadAsync("certificates", "denied");
        await act.Should().ThrowAsync<AmazonS3Exception>();
    }

    // SFTP

    [Fact]
    public async Task Sftp_says_clearly_that_public_objects_are_not_supported()
    {
        var sut = new SftpStorageService(new ConfigurationBuilder().Build(), new StorageConfiguration());

        Func<Task> upload = () => sut.UploadPublicObjectAsync("certificates", "k", new MemoryStream(), "text/plain");
        Func<Task> read = () => sut.OpenReadAsync("certificates", "k");

        (await upload.Should().ThrowAsync<NotSupportedException>()).Which.Message.Should().Contain("SFTP");
        await read.Should().ThrowAsync<NotSupportedException>();
    }
}
