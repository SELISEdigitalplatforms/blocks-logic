namespace DomainService.Sequence
{
    public interface ISequenceRepository
    {
        Task<long> GetNextSequenceNumberAsync(string context);
        Task<long> GetNextHexSequenceNumberAsync(string context);
        Task ResetSequenceNumberAsync(string context, long startNumber);
    }
}