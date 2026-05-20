using Blocks.Genesis;

namespace DomainService.Sequence
{ 
    public interface ISequenceService
    {
        Task<SequenceNumberQueryResponse> GetNextSequenceNumberAsync(SequenceNumberQuery query);
        Task<SequenceNumberHexQueryResponse> GetNextHexSequenceNumberAsync(SequenceNumberHexQuery query);
        Task<BaseResponse> ResetSequenceNumberAsync(ResetSequenceNumberRequest request);
    }
}