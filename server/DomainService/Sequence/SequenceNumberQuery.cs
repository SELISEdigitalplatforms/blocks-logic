using Blocks.Genesis;
namespace DomainService.Sequence
{
    public class SequenceNumberQuery
    {
        public string Context { get; set; } = string.Empty;
    }
    public class SequenceNumberQueryResponse : BaseResponse
    {
        public string Context { get; set; } = string.Empty;
        public long CurrentNumber { get; set; }
    }
}