namespace Mail.DomainService.Services
{
    /// <summary>
    /// A phone-as-email address is built by iam as "&lt;phone, spaces removed, + as 00&gt;@&lt;gateway domain&gt;".
    /// The phone part is matched back to the user's stored number, which may still hold spaces,
    /// dashes and a leading + or 00.
    /// </summary>
    public static class PhoneEmail
    {
        private const int MinDigits = 5;

        /// <summary>A Mongo regex for the stored PhoneNumber, or null when the address has no phone part.</summary>
        public static string? UserPhonePattern(string? address)
        {
            var at = address?.IndexOf('@') ?? -1;
            if (address is null || at <= 0)
            {
                return null;
            }

            var digits = address[..at].Replace("-", string.Empty, StringComparison.Ordinal);
            if (digits.StartsWith("00", StringComparison.Ordinal))
            {
                digits = digits[2..];
            }

            if (digits.Length < MinDigits || !digits.All(char.IsAsciiDigit))
            {
                return null;
            }

            return @"^\s*(\+|00)?\s*" + string.Join(@"[\s-]*", digits.ToCharArray()) + @"\s*$";
        }
    }
}
