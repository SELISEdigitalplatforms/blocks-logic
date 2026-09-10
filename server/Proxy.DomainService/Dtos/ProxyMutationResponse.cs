using System.Text.Json.Serialization;
using Blocks.Genesis;

namespace Proxy.DomainService.Dtos
{
    /// <summary>
    /// Result of a proxy control-plane mutation. Extends <see cref="BaseMutationResponse"/> (<c>itemId</c>,
    /// <c>errors</c>, <c>isSuccess</c>) with the stable <c>code</c> and human <c>message</c> from SPEC &sect;3.4.
    /// <see cref="HttpStatus"/> is consumed by <c>ProxyController</c> to choose the response status code and is
    /// not serialized.
    /// </summary>
    public sealed class ProxyMutationResponse : BaseMutationResponse
    {
        /// <summary>Stable failure code (see <see cref="Utils.ProxyErrorCodes"/>); <c>null</c> on success.</summary>
        public string? Code { get; set; }

        /// <summary>Human-readable message for failures that carry one (slug conflict, not found, deleted).</summary>
        public string? Message { get; set; }

        /// <summary>HTTP status the controller should return. 201 on create, 200 otherwise, 4xx on failure.</summary>
        [JsonIgnore]
        public int HttpStatus { get; set; } = 200;

        public static ProxyMutationResponse Success(string itemId, int httpStatus = 200) => new()
        {
            IsSuccess = true,
            ItemId = itemId,
            HttpStatus = httpStatus,
        };

        public static ProxyMutationResponse Failure(
            int httpStatus, string code, string message, IDictionary<string, string>? errors = null) => new()
        {
            IsSuccess = false,
            ItemId = null,
            HttpStatus = httpStatus,
            Code = code,
            Message = message,
            Errors = errors,
        };
    }
}
