using System.Text.RegularExpressions;

namespace Functions.DomainService.Events
{
    /// <summary>
    /// The rules of a function's trigger names: the action names that start it (the Action trigger).
    /// </summary>
    public static class FunctionEventCatalog
    {
        /// <summary>The most actions one function can subscribe to.</summary>
        public const int MaxTriggerEvents = 20;

        /// <summary>The most a received action's data may hold, in UTF-8 bytes.</summary>
        public const int MaxPayloadBytes = 64 * 1024;

        /// <summary>The shape of an action name a function may subscribe to (for example <c>iam.user.created</c>).</summary>
        public static readonly Regex NamePattern = new(
            @"^[a-z][a-z0-9_.-]{2,63}\z", RegexOptions.Compiled | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(50));

        public static bool IsValidName(string? name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            try
            {
                return NamePattern.IsMatch(name);
            }
            catch (RegexMatchTimeoutException)
            {
                return false;
            }
        }
    }
}
