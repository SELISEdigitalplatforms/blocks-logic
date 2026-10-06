using System.Text.Json;
using System.Threading.Channels;
using Blocks.FunctionRunner.Admission;
using Blocks.FunctionRunner.Contracts;
using Blocks.FunctionRunner.Options;
using Blocks.FunctionRunner.Sandbox;
using Microsoft.Extensions.Logging.Abstractions;

namespace Blocks.FunctionRunner.Tests
{
    /// <summary>
    /// A reusable container that does what a test scripts, line by line — the reuse protocol is
    /// the thing under test, and the Docker half is a pass-through (covered by the real-Docker
    /// test in <see cref="ReusableSandboxDockerTests"/>).
    /// </summary>
    internal sealed class ScriptedContainer : IReusableContainer
    {
        private readonly Channel<string> _lines = Channel.CreateUnbounded<string>();

        public ScriptedContainer(string? name = null) => Name = name ?? "blocks-fnwarm-" + Guid.NewGuid().ToString("N");

        public string Name { get; }

        /// <summary>Lines written once the container starts. Default: ready.</summary>
        public List<string> OnStart { get; } = [Lines.Ready()];

        /// <summary>What the sandbox answers to one envelope line (given its run id). Default: a clean success.</summary>
        public Func<string, IEnumerable<string>> OnCall { get; set; } =
            id => [Lines.Started(id), Lines.Result(id, "\"ok\""), Lines.Idle(id, clean: true)];

        public string? StartFailure { get; set; }

        /// <summary>Thrown from StartAsync, as the Engine client does on an HttpClient timeout.</summary>
        public Exception? StartThrows { get; set; }

        /// <summary>Awaited inside StartAsync, before anything is written: a slow start, or a hook.</summary>
        public Func<Task>? OnStarting { get; set; }

        /// <summary>The write never completes until cancelled: a sandbox that stopped reading stdin.</summary>
        public bool BlockWrites { get; set; }

        public bool ExitThrows { get; set; }
        public bool FailPause { get; set; }
        public bool FailUnpause { get; set; }
        public long? MemoryBytes { get; set; } = 40L * 1024 * 1024;
        public long CpuTotalMs { get; set; } = 100;
        public (int ExitCode, bool OomKilled)? Exit { get; set; } = (137, false);

        public List<string> Written { get; } = [];
        public int Pauses { get; private set; }
        public int Unpauses { get; private set; }
        public bool Paused { get; private set; }
        public bool Killed { get; private set; }
        public bool Disposed { get; private set; }

        public async Task<string?> StartAsync(CancellationToken token)
        {
            if (OnStarting is not null) await OnStarting().ConfigureAwait(false);
            if (StartThrows is not null) throw StartThrows;
            if (StartFailure is not null) return StartFailure;
            foreach (var line in OnStart) _lines.Writer.TryWrite(line);
            return null;
        }

        public async ValueTask<string?> ReadLineAsync(CancellationToken token)
        {
            while (await _lines.Reader.WaitToReadAsync(token).ConfigureAwait(false))
            {
                if (_lines.Reader.TryRead(out var line)) return line;
            }
            return null;
        }

        public async Task WriteLineAsync(string line, CancellationToken token)
        {
            if (Killed) throw new IOException("the container is gone");
            if (BlockWrites) await Task.Delay(Timeout.Infinite, token).ConfigureAwait(false);
            Written.Add(line);
            using var doc = JsonDocument.Parse(line);
            var id = doc.RootElement.GetProperty("run").GetProperty("id").GetString()!;
            foreach (var output in OnCall(id)) _lines.Writer.TryWrite(output);
        }

        /// <summary>Ends the output stream, as a sandbox that exits does.</summary>
        public void End() => _lines.Writer.TryComplete();

        public Task PauseAsync(CancellationToken token)
        {
            if (FailPause) throw new InvalidOperationException("pause failed");
            Pauses++;
            Paused = true;
            return Task.CompletedTask;
        }

        public Task UnpauseAsync(CancellationToken token)
        {
            if (FailUnpause) throw new InvalidOperationException("unpause failed");
            Unpauses++;
            Paused = false;
            return Task.CompletedTask;
        }

        public Task<(long? MemoryBytes, long? CpuTotalMs)> StatsAsync(CancellationToken token)
        {
            CpuTotalMs += 10;
            return Task.FromResult<(long?, long?)>((MemoryBytes, CpuTotalMs));
        }

        public Task KillAsync()
        {
            Killed = true;
            _lines.Writer.TryComplete();
            return Task.CompletedTask;
        }

        public Task<(int ExitCode, bool OomKilled)?> ExitStateAsync() =>
            ExitThrows ? throw new HttpRequestException("the Engine did not answer") : Task.FromResult(Exit);

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            Killed = true;
            _lines.Writer.TryComplete();
            return ValueTask.CompletedTask;
        }
    }

    /// <summary>Reuse-protocol lines as reuse.mjs writes them.</summary>
    internal static class Lines
    {
        public static string Ready() => """{"t":"ready","at":1}""";

        public static string Started(string id) =>
            JsonSerializer.Serialize(new { t = "started", at = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), call = id });

        public static string Log(string id, string msg, bool late = false) =>
            JsonSerializer.Serialize(new { t = "log", ts = "2026-10-06T00:00:00Z", level = "info", msg, call = id, late });

        public static string Result(string id, string rawValue) =>
            $$"""{"t":"result","ok":true,"value":{{rawValue}},"call":"{{id}}"}""";

        public static string Failure(string id, string code, string message) =>
            JsonSerializer.Serialize(new { t = "result", ok = false, code, message, call = id });

        public static string Idle(string id, bool clean, string[]? leftovers = null, bool late = false) =>
            JsonSerializer.Serialize(new { t = "idle", call = id, clean, leftovers = leftovers ?? [], late, rssBytes = 1 });

        public static string Fatal(string code, string message) =>
            JsonSerializer.Serialize(new { t = "fatal", code, message });

        /// <summary>What a 24-v1 bootstrap writes in reuse mode: it ignores the mode and finds no execution.json.</summary>
        public static string V1NoEnvelope() =>
            JsonSerializer.Serialize(new
            {
                t = "result",
                ok = false,
                code = ErrorCodes.RuntimeStartFailed,
                message = "cannot read the execution envelope at /run/blocks/execution.json: ENOENT: no such file or directory",
            });

        public static string Envelope(string id) => "{\"run\":{\"id\":\"" + id + "\"},\"input\":{},\"env\":{}}";
    }

    /// <summary>Hands out scripted containers and remembers them.</summary>
    internal sealed class ScriptedFactory(RunnerOptions options) : IReusableSandboxFactory
    {
        public List<ScriptedContainer> Created { get; } = [];

        /// <summary>Shapes each new container before it is used.</summary>
        public Action<ScriptedContainer>? Configure { get; set; }

        /// <summary>Like <see cref="Configure"/>, also told which container this is (0 = first).</summary>
        public Action<ScriptedContainer, int>? ConfigureNth { get; set; }

        public ReusableSandbox Create(WarmKey key, RunLimits limits)
        {
            var container = new ScriptedContainer();
            Configure?.Invoke(container);
            ConfigureNth?.Invoke(container, Created.Count);
            Created.Add(container);
            return new ReusableSandbox(container, options, NullLogger.Instance);
        }
    }

    /// <summary>A large, calm host.</summary>
    internal sealed class CalmHost : IHostSignals
    {
        public double Cores { get; set; } = 8;
        public long TotalMemoryBytes { get; set; } = 64L * 1024 * 1024 * 1024;
        public long AvailableMemoryBytes { get; set; } = 64L * 1024 * 1024 * 1024;
        public HostSignalSample Sample() => new(0, 0, 0, AvailableMemoryBytes);
    }

    /// <summary>A clock that moves only when told to.</summary>
    internal sealed class ManualTime : TimeProvider
    {
        private long _ticks = TimeSpan.TicksPerHour;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => _ticks;
        public void Advance(TimeSpan by) => _ticks += by.Ticks;
    }
}
