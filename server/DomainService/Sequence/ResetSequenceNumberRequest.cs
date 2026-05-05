using Blocks.Genesis;

namespace DomainService.Sequence
{
    public class ResetSequenceNumberRequest : IProjectKey
    {
        public required string Context { get; set; }
        public long Value { get; set; }
        public string? ProjectKey { get; set; }
    }
}