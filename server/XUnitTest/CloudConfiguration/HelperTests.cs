using CloudConfiguration.DomainService.Shared.Utilities;
using FluentAssertions;

namespace XUnitTest.CloudConfiguration
{
    public class HelperTests
    {
        [Theory]
        [InlineData("", "")]
        [InlineData("a", "*")]
        [InlineData("ab", "**")]
        [InlineData("abc", "a*c")]
        [InlineData("region", "r****n")]
        public void GetMaskedCloudStorageRegionEndPoint_MasksMiddle(string input, string expected)
        {
            Helper.GetMaskedCloudStorageRegionEndPoint(input).Should().Be(expected);
        }

        [Fact]
        public void GenerateAesKey_ReturnsBase64_256Bit()
        {
            var key = Helper.GenerateAesKey();
            var bytes = Convert.FromBase64String(key);
            bytes.Should().HaveCount(32);
        }

        [Fact]
        public void EncryptThenDecrypt_RoundTrips()
        {
            var key = Helper.GenerateAesKey();
            var cipher = Helper.Encrypt("secret-value", key);

            cipher.Should().NotBe("secret-value");
            Helper.TryDecrypt(cipher, key, out var plain).Should().BeTrue();
            plain.Should().Be("secret-value");
        }

        [Fact]
        public void TryDecrypt_WithWrongInput_ReturnsFalse()
        {
            Helper.TryDecrypt("not-base64!!", Helper.GenerateAesKey(), out var result).Should().BeFalse();
            result.Should().BeEmpty();
        }

        [Fact]
        public void TryDecrypt_WithWrongKey_DoesNotRecoverPlaintext()
        {
            var cipher = Helper.Encrypt("data", Helper.GenerateAesKey());
            var otherKey = Helper.GenerateAesKey();

            // A different key either fails outright (bad padding) or yields something
            // other than the original plaintext; it must never recover "data".
            var ok = Helper.TryDecrypt(cipher, otherKey, out var result);
            (ok && result == "data").Should().BeFalse();
        }
    }
}
