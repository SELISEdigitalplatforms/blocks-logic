using Blocks.Genesis;

namespace DomainService.Sequence
{
    public class ResetSequenceNumberRequest
    {
        public required string Context { get; set; }
        public long Value { get; set; }
    }
}