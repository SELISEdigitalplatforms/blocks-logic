using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace XUnitTest.TestHelpers
{
    /// <summary>
    /// A minimal plaintext SMTP server on 127.0.0.1 for tests. It offers AUTH PLAIN and LOGIN,
    /// records every decoded credential and counts accepted connections, so a test driving a
    /// MailKit client can see exactly which password reached the wire, or that no connection was
    /// opened at all. Every other command is accepted; mail content is discarded.
    /// </summary>
    /// <remarks>
    /// Every session is bounded, so the server cannot hang a test run:
    /// <list type="bullet">
    /// <item>each read and write must finish within <see cref="IoTimeout"/>; a stalled exchange
    /// closes the connection (so the client under test fails within seconds, not after its own
    /// 100-120 s timeout);</item>
    /// <item>a line longer than <see cref="MaxLineLength"/> characters or more than
    /// <see cref="MaxDataLines"/> lines of DATA ends the session with a 500/421 reply, and so does
    /// reaching <see cref="MaxCommandsPerSession"/> commands without a QUIT;</item>
    /// <item>an AUTH payload that is not valid Base64 is answered with 501 and records no credential.</item>
    /// </list>
    /// Each of these is recorded in <see cref="Faults"/>, which <see cref="Describe"/> reports.
    /// </remarks>
    internal sealed class FakeSmtpServer : IDisposable
    {
        public const int MaxLineLength = 4096;
        public const int MaxCommandsPerSession = 100;
        public const int MaxDataLines = 10_000;

        private readonly TcpListener _listener;
        private readonly CancellationTokenSource _cts = new();
        private readonly ConcurrentQueue<(string User, string Password)> _credentials = new();
        private readonly ConcurrentQueue<string> _faults = new();
        private readonly ConcurrentDictionary<TcpClient, byte> _clients = new();
        private int _connections;
        private int _disposed;

        public static readonly TimeSpan DefaultIoTimeout = TimeSpan.FromSeconds(10);

        public FakeSmtpServer(TimeSpan? ioTimeout = null)
        {
            IoTimeout = ioTimeout ?? DefaultIoTimeout;
            _listener = new TcpListener(IPAddress.Loopback, 0);
            _listener.Start();
            Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            _ = AcceptLoopAsync();
        }

        public string Host => "127.0.0.1";
        public int Port { get; }
        public TimeSpan IoTimeout { get; }
        public int Connections => Volatile.Read(ref _connections);
        public IReadOnlyList<(string User, string Password)> Credentials => _credentials.ToList();

        /// <summary>
        /// Stalled exchanges and protocol faults, e.g. "FakeSmtpServer: timed out after 10s waiting
        /// for the exchange after EHLO; connection closed." or "FakeSmtpServer: malformed Base64 in
        /// the AUTH PLAIN payload; no credential recorded."
        /// </summary>
        public IReadOnlyList<string> Faults => _faults.ToList();

        /// <summary>A failure reason for assertions: the recorded faults, or a note that there were none.</summary>
        public string Describe() =>
            _faults.IsEmpty ? $"FakeSmtpServer saw {Connections} connection(s) and no stalled exchange or protocol fault" : string.Join(" ", _faults);

        private async Task AcceptLoopAsync()
        {
            while (!_cts.IsCancellationRequested)
            {
                TcpClient client;
                try
                {
                    client = await _listener.AcceptTcpClientAsync(_cts.Token);
                }
                catch (Exception) when (_cts.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception ex)
                {
                    _faults.Enqueue($"FakeSmtpServer: the accept loop stopped ({ex.GetType().Name}); no further connections accepted.");
                    return;
                }

                Interlocked.Increment(ref _connections);
                _clients.TryAdd(client, 0);
                _ = Task.Run(() => ServeAsync(client));
            }
        }

        private async Task ServeAsync(TcpClient client)
        {
            try
            {
                using (client)
                {
                    var stream = client.GetStream();
                    using var reader = new StreamReader(stream, Encoding.ASCII);
                    await using var writer = new StreamWriter(stream, Encoding.ASCII) { NewLine = "\r\n", AutoFlush = true };

                    // Faults are recorded inside the session, before the reader and writer are
                    // disposed and the connection closes, so a client that sees the close also sees
                    // the fault in Describe().
                    await ServeSessionAsync(stream, new Io(reader, writer, IoTimeout, _cts.Token));
                }
            }
            catch (Exception)
            {
                // Closing a connection that is already gone; nothing to record.
            }
            finally
            {
                _clients.TryRemove(client, out _);
            }
        }

        private async Task ServeSessionAsync(NetworkStream stream, Io io)
        {
            var step = "the greeting";
            try
            {
                await io.WriteLineAsync("220 fake.smtp ESMTP");
                step = "the first command";
                for (var commands = 0; commands < MaxCommandsPerSession; commands++)
                {
                    var line = await io.ReadLineAsync();
                    if (line is null)
                    {
                        return;
                    }

                    var verb = line.Split(' ')[0].ToUpperInvariant();
                    step = $"the exchange after {verb}";
                    switch (verb)
                    {
                        case "EHLO":
                            await io.WriteLineAsync("250-fake.smtp");
                            await io.WriteLineAsync("250-AUTH PLAIN LOGIN");
                            await io.WriteLineAsync("250 OK");
                            break;
                        case "AUTH":
                            await AuthenticateAsync(line, io);
                            break;
                        case "DATA":
                            await io.WriteLineAsync("354 End data with <CR><LF>.<CR><LF>");
                            if (!await DiscardDataAsync(io))
                            {
                                return;
                            }
                            await io.WriteLineAsync("250 OK queued");
                            break;
                        case "QUIT":
                            await io.WriteLineAsync("221 Bye");
                            return;
                        default:
                            await io.WriteLineAsync("250 OK");
                            break;
                    }
                }

                _faults.Enqueue($"FakeSmtpServer: the session reached its limit of {MaxCommandsPerSession} commands; connection closed.");
                await io.WriteLineAsync("421 Too many commands, closing connection");
            }
            catch (TimeoutException)
            {
                _faults.Enqueue($"FakeSmtpServer: timed out after {IoTimeout.TotalSeconds:0.###}s waiting for {step}; connection closed.");
            }
            catch (LineTooLongException)
            {
                _faults.Enqueue($"FakeSmtpServer: a line longer than {MaxLineLength} characters while waiting for {step}; connection closed.");
                await TryWriteAsync(stream, "500 Line too long");
            }
            catch (Exception) when (_cts.IsCancellationRequested)
            {
                // Disposed mid-exchange.
            }
            catch (Exception)
            {
                // The client went away; nothing to record.
            }
        }

        /// <summary>Reads and discards DATA up to the lone "."; false when the session must end.</summary>
        private async Task<bool> DiscardDataAsync(Io io)
        {
            for (var lines = 0; lines <= MaxDataLines; lines++)
            {
                var line = await io.ReadLineAsync();
                if (line is null)
                {
                    return false;
                }

                if (line == ".")
                {
                    return true;
                }
            }

            _faults.Enqueue($"FakeSmtpServer: more than {MaxDataLines} lines of DATA; connection closed.");
            await io.WriteLineAsync("421 Message too long, closing connection");
            return false;
        }

        private async Task AuthenticateAsync(string line, Io io)
        {
            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var mechanism = parts.Length > 1 ? parts[1].ToUpperInvariant() : string.Empty;
            (string User, string Password) credential;

            if (mechanism == "PLAIN")
            {
                var payload = parts.Length > 2 ? parts[2] : null;
                if (payload is null)
                {
                    await io.WriteLineAsync("334 ");
                    payload = await io.ReadLineAsync() ?? string.Empty;
                }

                if (!TryDecode(payload, out var decoded))
                {
                    await RejectMalformedAsync(io, "the AUTH PLAIN payload");
                    return;
                }

                // authzid \0 authcid \0 password
                var fields = decoded.Split('\0');
                credential = (fields.ElementAtOrDefault(1) ?? string.Empty, fields.ElementAtOrDefault(2) ?? string.Empty);
            }
            else if (mechanism == "LOGIN")
            {
                var encodedUser = parts.Length > 2 ? parts[2] : null;
                if (encodedUser is null)
                {
                    await io.WriteLineAsync("334 VXNlcm5hbWU6");
                    encodedUser = await io.ReadLineAsync() ?? string.Empty;
                }

                if (!TryDecode(encodedUser, out var user))
                {
                    await RejectMalformedAsync(io, "the AUTH LOGIN username");
                    return;
                }

                await io.WriteLineAsync("334 UGFzc3dvcmQ6");
                if (!TryDecode(await io.ReadLineAsync() ?? string.Empty, out var password))
                {
                    await RejectMalformedAsync(io, "the AUTH LOGIN password");
                    return;
                }

                credential = (user, password);
            }
            else
            {
                await io.WriteLineAsync("504 Unrecognized authentication type");
                return;
            }

            _credentials.Enqueue(credential);
            await io.WriteLineAsync("235 Authentication successful");
        }

        private Task RejectMalformedAsync(Io io, string what)
        {
            _faults.Enqueue($"FakeSmtpServer: malformed Base64 in {what}; no credential recorded.");
            return io.WriteLineAsync("501 Malformed Base64");
        }

        /// <summary>Decodes client-supplied Base64 without throwing; false when it is malformed.</summary>
        private static bool TryDecode(string base64, out string decoded)
        {
            var trimmed = base64.Trim();
            var buffer = new byte[(trimmed.Length / 4 + 1) * 3];
            if (Convert.TryFromBase64String(trimmed, buffer, out var written))
            {
                decoded = Encoding.UTF8.GetString(buffer, 0, written);
                return true;
            }

            decoded = string.Empty;
            return false;
        }

        /// <summary>A best-effort final reply on a session that is being closed.</summary>
        private async Task TryWriteAsync(NetworkStream stream, string line)
        {
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);
                timeout.CancelAfter(IoTimeout);
                await stream.WriteAsync(Encoding.ASCII.GetBytes(line + "\r\n"), timeout.Token);
            }
            catch (Exception)
            {
                // The session is ending anyway.
            }
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 1)
            {
                return;
            }

            _cts.Cancel();
            _listener.Stop();
            foreach (var client in _clients.Keys)
            {
                client.Dispose();
            }

            _clients.Clear();
            _cts.Dispose();
        }

        private sealed class LineTooLongException : Exception
        {
        }

        /// <summary>
        /// Line I/O where every read and write must finish within the server's IoTimeout, and no
        /// line may exceed <see cref="MaxLineLength"/> characters (so a peer cannot make the server
        /// buffer an unbounded line).
        /// </summary>
        private sealed class Io
        {
            private readonly StreamReader _reader;
            private readonly StreamWriter _writer;
            private readonly TimeSpan _timeout;
            private readonly CancellationToken _stopping;
            private readonly char[] _buffer = new char[1024];
            private int _start;
            private int _end;

            public Io(StreamReader reader, StreamWriter writer, TimeSpan timeout, CancellationToken stopping)
            {
                _reader = reader;
                _writer = writer;
                _timeout = timeout;
                _stopping = stopping;
            }

            public Task<string?> ReadLineAsync() => Bounded(ReadBoundedLineAsync);

            public Task WriteLineAsync(string line) => Bounded(async ct =>
            {
                await _writer.WriteLineAsync(line.AsMemory(), ct);
                return true;
            });

            /// <summary>One line without its CR LF, or null at end of stream.</summary>
            private async Task<string?> ReadBoundedLineAsync(CancellationToken cancellationToken)
            {
                var line = new StringBuilder();

                // Bounded: every pass that does not return adds at least one character to `line`,
                // and a line past MaxLineLength throws.
                while (true)
                {
                    if (_start == _end)
                    {
                        _start = 0;
                        _end = await _reader.ReadAsync(_buffer.AsMemory(), cancellationToken);
                        if (_end == 0)
                        {
                            return line.Length > 0 ? line.ToString() : null;
                        }
                    }

                    var newline = Array.IndexOf(_buffer, '\n', _start, _end - _start);
                    var stop = newline < 0 ? _end : newline;
                    line.Append(_buffer, _start, stop - _start);
                    _start = newline < 0 ? _end : newline + 1;

                    if (newline >= 0)
                    {
                        if (line.Length > 0 && line[^1] == '\r')
                        {
                            line.Length--;
                        }

                        return line.Length > MaxLineLength ? throw new LineTooLongException() : line.ToString();
                    }

                    // One extra character of room for a CR whose LF has not arrived yet.
                    if (line.Length > MaxLineLength + 1)
                    {
                        throw new LineTooLongException();
                    }
                }
            }

            private async Task<T> Bounded<T>(Func<CancellationToken, Task<T>> operation)
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_stopping);
                timeout.CancelAfter(_timeout);
                try
                {
                    return await operation(timeout.Token);
                }
                catch (OperationCanceledException) when (!_stopping.IsCancellationRequested)
                {
                    throw new TimeoutException();
                }
            }
        }
    }
}
