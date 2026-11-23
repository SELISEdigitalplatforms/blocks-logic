using System.Text.Json.Nodes;
using Common.InternalService.Storage;
using FluentAssertions;

namespace XUnitTest.Services;

/// <summary>
/// The shared rules for public objects (I02-WP9) in blocks-logic's copy: which container names and keys are accepted, the S3
/// bucket policy that makes a bucket's objects readable without credentials (and how it merges into a policy that already
/// exists), and the permanent URL of an object.
/// </summary>
public class PublicObjectRulesTests
{

    [Theory]
    [InlineData("certificates")]
    [InlineData("abc")]
    [InlineData("tenant-1-public")]
    public void A_valid_container_name_is_accepted(string container)
    {
        var act = () => PublicObjectRules.Validate(container, "key");
        act.Should().NotThrow();
    }

    [Theory]
    [InlineData("")]
    [InlineData("ab")]
    [InlineData("Certificates")]
    [InlineData("-certificates")]
    [InlineData("certificates-")]
    [InlineData("cert.ificates")]
    [InlineData("cert_ificates")]
    [InlineData("certs/../other")]
    public void An_invalid_container_name_is_refused(string container)
    {
        var act = () => PublicObjectRules.Validate(container, "key");
        act.Should().Throw<PublicObjectStorageException>().Which.ErrorCode.Should().Be(PublicObjectStorageException.InvalidContainerCode);
    }

    [Theory]
    [InlineData("tenant-1")]
    [InlineData("tenant-1_3rdparty_idp.pfx")]
    [InlineData("a/b/c.crt")]
    public void A_valid_key_is_accepted(string key)
    {
        var act = () => PublicObjectRules.Validate("certificates", key);
        act.Should().NotThrow();
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("/leading")]
    [InlineData("trailing/")]
    [InlineData("a//b")]
    [InlineData("a/../b")]
    [InlineData("..")]
    [InlineData("a/./b")]
    [InlineData("line\nbreak")]
    public void An_invalid_key_is_refused(string key)
    {
        var act = () => PublicObjectRules.Validate("certificates", key);
        act.Should().Throw<PublicObjectStorageException>().Which.ErrorCode.Should().Be(PublicObjectStorageException.InvalidKeyCode);
    }

    [Fact]
    public void A_key_over_1024_bytes_is_refused()
    {
        var act = () => PublicObjectRules.Validate("certificates", new string('k', 1025));
        act.Should().Throw<PublicObjectStorageException>();
    }

    [Fact]
    public void The_public_read_policy_allows_only_GetObject_on_the_objects_of_that_bucket()
    {
        var policy = JsonNode.Parse(PublicObjectRules.BuildPublicReadPolicy("certificates"))!;

        var statements = policy["Statement"]!.AsArray();
        statements.Should().HaveCount(1);
        var statement = statements[0]!;
        statement["Effect"]!.GetValue<string>().Should().Be("Allow");
        statement["Principal"]!.GetValue<string>().Should().Be("*");
        statement["Action"]!.AsArray().Select(a => a!.GetValue<string>()).Should().Equal("s3:GetObject");
        statement["Resource"]!.AsArray().Select(r => r!.GetValue<string>()).Should().Equal("arn:aws:s3:::certificates/*");
    }

    [Fact]
    public void Adding_the_statement_keeps_the_statements_a_bucket_already_has()
    {
        const string existing = """{"Version":"2012-10-17","Statement":[{"Sid":"Other","Effect":"Allow","Principal":{"AWS":"arn:aws:iam::1:root"},"Action":"s3:ListBucket","Resource":"arn:aws:s3:::certificates"}]}""";

        var merged = PublicObjectRules.AddPublicReadStatement(existing, "certificates");

        var sids = JsonNode.Parse(merged!)!["Statement"]!.AsArray().Select(s => s!["Sid"]!.GetValue<string>());
        sids.Should().Equal("Other", PublicObjectRules.PublicReadStatementId);
    }

    [Fact]
    public void Adding_the_statement_is_a_no_op_when_the_policy_already_allows_anonymous_reads()
    {
        var once = PublicObjectRules.BuildPublicReadPolicy("certificates");

        PublicObjectRules.AddPublicReadStatement(once, "certificates").Should().BeNull();
    }

    [Fact]
    public void A_statement_for_another_bucket_or_a_condition_does_not_count_as_public_read()
    {
        var otherBucket = PublicObjectRules.BuildPublicReadPolicy("other-bucket");
        PublicObjectRules.AddPublicReadStatement(otherBucket, "certificates").Should().NotBeNull();

        const string conditional = """{"Version":"2012-10-17","Statement":{"Effect":"Allow","Principal":"*","Action":"s3:GetObject","Resource":"arn:aws:s3:::certificates/*","Condition":{"IpAddress":{"aws:SourceIp":"10.0.0.0/8"}}}}""";
        var merged = PublicObjectRules.AddPublicReadStatement(conditional, "certificates");
        merged.Should().NotBeNull();
        JsonNode.Parse(merged!)!["Statement"]!.AsArray().Should().HaveCount(2);
    }

    [Fact]
    public void A_single_statement_object_is_accepted_and_kept()
    {
        const string single = """{"Version":"2012-10-17","Statement":{"Sid":"One","Effect":"Deny","Principal":"*","Action":"s3:DeleteObject","Resource":"arn:aws:s3:::certificates/*"}}""";

        var merged = PublicObjectRules.AddPublicReadStatement(single, "certificates");

        JsonNode.Parse(merged!)!["Statement"]!.AsArray().Select(s => s!["Sid"]!.GetValue<string>())
            .Should().Equal("One", PublicObjectRules.PublicReadStatementId);
    }

    [Fact]
    public void The_url_of_an_object_on_a_store_with_its_own_endpoint_is_path_style()
    {
        PublicObjectRules.BuildS3ObjectUrl("http://s3:8333/", null, "certificates", "tenant-1_3rdparty.pfx")
            .Should().Be("http://s3:8333/certificates/tenant-1_3rdparty.pfx");
    }

    [Fact]
    public void The_url_of_an_object_on_AWS_is_virtual_hosted_and_each_segment_is_escaped()
    {
        PublicObjectRules.BuildS3ObjectUrl(null, "s3.eu-central-1.amazonaws.com", "certificates", "a b/c+d.pem")
            .Should().Be("https://certificates.s3.eu-central-1.amazonaws.com/a%20b/c%2Bd.pem");
    }

    [Fact]
    public void The_url_cannot_be_built_without_an_endpoint_or_a_region()
    {
        var act = () => PublicObjectRules.BuildS3ObjectUrl(null, null, "certificates", "key");
        act.Should().Throw<PublicObjectStorageException>();
    }
}
