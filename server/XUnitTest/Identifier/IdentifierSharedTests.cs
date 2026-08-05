using DomainService.Shared;
using FluentAssertions;

namespace XUnitTest.Identifier
{
    public class EncryptionHelperTests
    {
        [Fact]
        public void EncryptThenDecrypt_RoundTripsThePlainText()
        {
            const string plainText = "Endpoint=sb://example.servicebus.windows.net/;SharedAccessKeyName=policy";

            var cipherText = EncryptionHelper.Encrypt(plainText, "tenant-salt");

            cipherText.Should().NotBe(plainText);
            EncryptionHelper.Decrypt(cipherText, "tenant-salt").Should().Be(plainText);
        }

        [Fact]
        public void Encrypt_ProducesADifferentCipherTextEachTime()
        {
            var first = EncryptionHelper.Encrypt("same value", "tenant-salt");
            var second = EncryptionHelper.Encrypt("same value", "tenant-salt");

            first.Should().NotBe(second, "a fresh IV is generated for every call");
            EncryptionHelper.Decrypt(first, "tenant-salt").Should().Be(EncryptionHelper.Decrypt(second, "tenant-salt"));
        }

        [Fact]
        public void Encrypt_KeyLongerThanThirtyTwoBytes_IsTruncatedConsistently()
        {
            var longKey = new string('k', 64);

            var cipherText = EncryptionHelper.Encrypt("payload", longKey);

            EncryptionHelper.Decrypt(cipherText, longKey).Should().Be("payload");
            EncryptionHelper.Decrypt(cipherText, new string('k', 32)).Should().Be("payload");
        }

        [Theory]
        [InlineData("")]
        [InlineData(null)]
        public void Encrypt_EmptyOrNullPlainText_ReturnsInput(string? plainText)
        {
            EncryptionHelper.Encrypt(plainText!, "tenant-salt").Should().Be(plainText);
        }

        [Theory]
        [InlineData("")]
        [InlineData(null)]
        public void Decrypt_EmptyOrNullCipherText_ReturnsInput(string? cipherText)
        {
            EncryptionHelper.Decrypt(cipherText!, "tenant-salt").Should().Be(cipherText);
        }
    }
}
