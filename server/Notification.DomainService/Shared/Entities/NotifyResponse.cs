using Blocks.Genesis;

namespace DomainService.Shared
{
    /// <summary>The result of a send. <see cref="Message"/> says why a successful send reached no one.</summary>
    public class NotifyResponse : BaseResponse
    {
        /// <summary>Set to <see cref="NoReceivers"/> when the configuration audience left no one to send to.</summary>
        public string? Message { get; set; }

        public const string NoReceivers = "no_receivers";
    }
}
