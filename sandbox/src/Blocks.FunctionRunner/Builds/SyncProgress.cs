namespace Blocks.FunctionRunner.Builds
{
    /// <summary>
    /// An <see cref="IProgress{T}"/> that runs its callback on the reporting thread, before Docker.DotNet
    /// reads the next message. <see cref="Progress{T}"/> posts each callback to the thread pool instead, so
    /// a build or push can return before its last message (the error) has been seen — a failed build then
    /// reads as a success.
    /// </summary>
    public sealed class SyncProgress<T> : IProgress<T>
    {
        private readonly Action<T> _handler;

        public SyncProgress(Action<T> handler) => _handler = handler;

        public void Report(T value) => _handler(value);
    }
}
