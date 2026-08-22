namespace Common.InternalService.Shared.Utilities
{
    public static class Helper
    {
        public static string GetMaskedCloudStorageRegionEndPoint(string endPoint)
        {
            if (string.IsNullOrEmpty(endPoint))
                return string.Empty;

            ReadOnlySpan<char> span = endPoint.AsSpan();
            if (span.Length <= 2)
                return new string('*', span.Length);

            return $"{span[0]}{new string('*', span.Length - 2)}{span[^1]}";
        }
    }
}
