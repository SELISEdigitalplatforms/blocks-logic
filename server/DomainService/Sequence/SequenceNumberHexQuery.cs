using Blocks.Genesis;
namespace DomainService.Sequence
{
    public class SequenceNumberHexQuery
    {
        public string Context { get; set; } = string.Empty;
    }
    public class SequenceNumberHexQueryResponse : BaseResponse
    {
        public string Context { get; set; } = string.Empty;
        public string CurrentNumber { get; set; } = string.Empty;
    }
}