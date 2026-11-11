using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using Mail.DomainService.Mails;
using Mail.DomainService.Mails.Office365;
using MailKit.Security;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MimeKit;

namespace XUnitTest.Mail
{
    /// <summary>
    /// A clock that moves only when told to. Timers (the attempt deadline and the waits between
    /// attempts) fire synchronously inside <see cref="Advance"/>, so a 160 s retry path runs in
    /// milliseconds and every timing assertion is exact.
    /// </summary>
    internal sealed class ManualTimeProvider : TimeProvider
    {
        private static readonly DateTimeOffset Origin = new(2026, 11, 11, 0, 0, 0, TimeSpan.Zero);

        private readonly object _gate = new();
        private readonly List<ManualTimer> _timers = [];
        private readonly List<TimeSpan> _requested = [];
        private TimeSpan _elapsed;

        public TimeSpan Elapsed
        {
            get { lock (_gate) return _elapsed; }
        }

        /// <summary>Every due time a timer was created with, in order.</summary>
        public IReadOnlyList<TimeSpan> RequestedDueTimes
        {
            get { lock (_gate) return _requested.ToList(); }
        }

        /// <summary>The waits between attempts: every requested timer except the attempt deadlines.</summary>
        public IReadOnlyList<TimeSpan> Waits =>
            RequestedDueTimes.Where(due => due != TimeSpan.FromSeconds(30)).ToList();

        public override DateTimeOffset GetUtcNow() => Origin + Elapsed;

        public override long GetTimestamp() => Elapsed.Ticks;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = new ManualTimer(this, callback, state);
            lock (_gate)
            {
                _requested.Add(dueTime);
                _timers.Add(timer);
            }

            timer.Change(dueTime, period);
            return timer;
        }

        /// <summary>Moves the clock forward and fires every timer that has come due.</summary>
        public void Advance(TimeSpan by)
        {
            lock (_gate) _elapsed += by;
            FireDueTimers();
        }

        /// <summary>
        /// Waits (in real time, bounded) until a timer is pending that will fire exactly
        /// <paramref name="remaining"/> from now: the sender has started that wait.
        /// </summary>
        public async Task WaitForTimerAsync(TimeSpan remaining)
        {
            var giveUp = DateTime.UtcNow.AddSeconds(10);
            while (!HasPendingTimer(remaining))
            {
                if (DateTime.UtcNow > giveUp)
                {
                    throw new TimeoutException($"No timer due in {remaining} was started; requested so far: {string.Join(", ", RequestedDueTimes)}");
                }

                await Task.Delay(5);
            }
        }

        private bool HasPendingTimer(TimeSpan remaining)
        {
            lock (_gate) return _timers.Exists(t => t.DueAt is { } due && due - _elapsed == remaining);
        }

        private void FireDueTimers()
        {
            while (true)
            {
                ManualTimer? next;
                lock (_gate)
                {
                    next = _timers
                        .Where(t => t.DueAt is { } due && due <= _elapsed)
                        .OrderBy(t => t.DueAt)
                        .FirstOrDefault();
                    if (next is null) return;
                    next.DueAt = next.Period is { } period ? _elapsed + period : null;
                }

                next.Fire();
            }
        }

        private void Reschedule(ManualTimer timer, TimeSpan dueTime, TimeSpan period)
        {
            lock (_gate)
            {
                timer.DueAt = dueTime == Timeout.InfiniteTimeSpan ? null : _elapsed + dueTime;
                timer.Period = period == Timeout.InfiniteTimeSpan || period == TimeSpan.Zero ? null : period;
            }

            FireDueTimers();
        }

        private void Remove(ManualTimer timer)
        {
            lock (_gate) _timers.Remove(timer);
        }

        private sealed class ManualTimer(ManualTimeProvider owner, TimerCallback callback, object? state) : ITimer
        {
            public TimeSpan? DueAt { get; set; }
            public TimeSpan? Period { get; set; }

            public void Fire() => callback(state);

            public bool Change(TimeSpan dueTime, TimeSpan period)
            {
                owner.Reschedule(this, dueTime, period);
                return true;
            }

            public void Dispose() => owner.Remove(this);

            public ValueTask DisposeAsync()
            {
                Dispose();
                return ValueTask.CompletedTask;
            }
        }
    }

    /// <summary>
    /// A scripted SMTP session. Each phase moves the manual clock by a fixed amount, so the
    /// logged timings are exact: TCP 40 ms, handshake to certificate check 150 ms, certificate
    /// check to ready 60 ms, login 200 ms.
    /// </summary>
    internal sealed class ScriptedSmtpSession : IMailKitSmtpClient
    {
        public static readonly TimeSpan TcpTime = TimeSpan.FromMilliseconds(40);
        public static readonly TimeSpan HandshakeTime = TimeSpan.FromMilliseconds(150);
        public static readonly TimeSpan ReadyTime = TimeSpan.FromMilliseconds(60);
        public static readonly TimeSpan AuthTime = TimeSpan.FromMilliseconds(200);

        private readonly ManualTimeProvider _time;

        public ScriptedSmtpSession(ManualTimeProvider time)
        {
            _time = time;
            Diagnostics = new SmtpConnectionDiagnostics(time);
        }

        public SmtpConnectionDiagnostics Diagnostics { get; }

        // ---- script ----

        /// <summary>Runs at the start of connect, once the host is known.</summary>
        public Action<ScriptedSmtpSession>? BeforeConnect { get; set; }

        /// <summary>Thrown before the TCP connection opens (DNS or TCP failure).</summary>
        public Exception? FailBeforeTcp { get; set; }

        /// <summary>The connect never finishes; the clock runs to the attempt deadline.</summary>
        public bool StallConnect { get; set; }

        /// <summary>The certificate check's input; null means the handshake never reaches it.</summary>
        public SslPolicyErrors? CertificateErrors { get; set; } = SslPolicyErrors.None;

        public X509Chain? Chain { get; set; }

        /// <summary>Thrown after the certificate check, before the connect completes.</summary>
        public Exception? FailConnect { get; set; }

        public Exception? FailAuthenticate { get; set; }

        /// <summary>The login never finishes; the clock runs past the attempt deadline.</summary>
        public bool StallAuthenticate { get; set; }

        public Exception? FailSend { get; set; }
        public Exception? FailDisconnect { get; set; }

        // ---- record ----

        public string? Host { get; private set; }
        public int Port { get; private set; }
        public SecureSocketOptions? SocketOptions { get; private set; }
        public TimeSpan? ConnectStartedAt { get; private set; }
        public TimeSpan? DisposedAt { get; private set; }
        public (string User, string Password)? Credentials { get; private set; }
        public int ConnectCount { get; private set; }
        public int AuthenticateCount { get; private set; }
        public int SendCount { get; private set; }
        public MimeMessage? Sent { get; private set; }
        public bool Disconnected { get; private set; }
        public bool Disposed => DisposedAt is not null;

        public async Task ConnectAsync(string host, int port, SecureSocketOptions socketOptions, CancellationToken cancellationToken)
        {
            ConnectCount++;
            Host = host;
            Port = port;
            SocketOptions = socketOptions;
            ConnectStartedAt = _time.Elapsed;
            BeforeConnect?.Invoke(this);

            if (StallConnect)
            {
                _time.Advance(TimeSpan.FromSeconds(30));
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }

            if (FailBeforeTcp is not null) throw FailBeforeTcp;
            _time.Advance(TcpTime);
            Diagnostics.MarkTcpConnected();

            if (CertificateErrors is { } errors)
            {
                _time.Advance(HandshakeTime);
                if (!Diagnostics.RecordCertificateCheck(Chain, errors))
                {
                    throw new SslHandshakeException("The remote certificate was rejected.");
                }
            }

            if (FailConnect is not null) throw FailConnect;
            _time.Advance(ReadyTime);
            Diagnostics.MarkConnected();
        }

        public Task AuthenticateAsync(string userName, string password, CancellationToken cancellationToken)
        {
            AuthenticateCount++;
            Credentials = (userName, password);
            Diagnostics.MarkAuthenticationStarted();
            if (StallAuthenticate)
            {
                // The attempt started 250 ms ago, so 29.75 s more reaches its 30 s deadline.
                _time.Advance(TimeSpan.FromSeconds(30) - TcpTime - HandshakeTime - ReadyTime);
                return Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }

            if (FailAuthenticate is not null) throw FailAuthenticate;
            _time.Advance(AuthTime);
            Diagnostics.MarkAuthenticated();
            return Task.CompletedTask;
        }

        public Task AuthenticateAsync(SaslMechanism mechanism) =>
            throw new NotSupportedException("A password record never presents a token.");

        public Task SendAsync(MimeMessage message)
        {
            SendCount++;
            Sent = message;
            if (FailSend is not null) throw FailSend;
            return Task.CompletedTask;
        }

        public Task DisconnectAsync(bool quit)
        {
            if (FailDisconnect is not null) throw FailDisconnect;
            Disconnected = true;
            return Task.CompletedTask;
        }

        public void Dispose() => DisposedAt ??= _time.Elapsed;
    }

    /// <summary>Hands out a new scripted session per attempt and keeps them all.</summary>
    internal sealed class ScriptedSessions(ManualTimeProvider time, Action<int, ScriptedSmtpSession>? script)
    {
        private readonly List<ScriptedSmtpSession> _sessions = [];

        public IReadOnlyList<ScriptedSmtpSession> All
        {
            get { lock (_sessions) return _sessions.ToList(); }
        }

        public ScriptedSmtpSession Create()
        {
            var session = new ScriptedSmtpSession(time);
            int number;
            lock (_sessions)
            {
                _sessions.Add(session);
                number = _sessions.Count;
            }

            script?.Invoke(number, session);
            return session;
        }
    }

    /// <summary>The real MailKit sender, on scripted sessions and the manual clock.</summary>
    internal sealed class ScriptedMailKitSmtpClient(
        ILogger<MailKitSmtpClient> logger,
        IConfiguration configuration,
        ManualTimeProvider time,
        Action<int, ScriptedSmtpSession>? script = null)
        : MailKitSmtpClient(logger, configuration, time)
    {
        private readonly ScriptedSessions _sessions = new(time, script);

        public IReadOnlyList<ScriptedSmtpSession> Sessions => _sessions.All;

        protected override IMailKitSmtpClient CreateSmtpClient() => _sessions.Create();
    }

    /// <summary>The real Office 365 SMTP sender, on scripted sessions and the manual clock.</summary>
    internal sealed class ScriptedOffice365SmtpClient(
        ILogger<Office365SmtpClient> logger,
        ManualTimeProvider time,
        Action<int, ScriptedSmtpSession>? script = null)
        : Office365SmtpClient(logger, time)
    {
        private readonly ScriptedSessions _sessions = new(time, script);

        public IReadOnlyList<ScriptedSmtpSession> Sessions => _sessions.All;

        protected override IMailKitSmtpClient CreateSmtpClient() => _sessions.Create();
    }

    /// <summary>Keeps each log entry with its level, rendered message and full text.</summary>
    internal sealed class LevelLogger<T> : ILogger<T>
    {
        private readonly List<(LogLevel Level, string Message, string Full)> _entries = [];

        public IReadOnlyList<(LogLevel Level, string Message, string Full)> Entries
        {
            get { lock (_entries) return _entries.ToList(); }
        }

        public IReadOnlyList<string> At(LogLevel level) =>
            Entries.Where(e => e.Level == level).Select(e => e.Message).ToList();

        public IReadOnlyList<string> Containing(string text) =>
            Entries.Where(e => e.Message.Contains(text, StringComparison.Ordinal)).Select(e => e.Message).ToList();

        /// <summary>Every message, structured value and exception text, for leak assertions.</summary>
        public string All => string.Join("\n", Entries.Select(e => e.Full));

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            var message = formatter(state, exception);
            var parts = new List<string> { message };
            if (state is IEnumerable<KeyValuePair<string, object?>> pairs)
            {
                parts.AddRange(pairs.Select(p => $"{p.Key}={p.Value}"));
            }

            if (exception is not null) parts.Add(exception.ToString());
            lock (_entries) _entries.Add((logLevel, message, string.Join(" | ", parts)));
        }
    }
}
