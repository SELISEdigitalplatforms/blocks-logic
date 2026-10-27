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
    /// records every decoded credential and counts accepted connections, so a test can see exactly
    /// which password reached the wire (MailKit and System.Net.Mail alike) or that no connection
    /// was opened at all. Every command is accepted; mail content is discarded.
    /// Each read and write is bounded by <see cref="IoTimeout"/>: a stalled exchange closes the
    /// connection (so the client under test fails within seconds, not after its own 100-120 s
    /// timeout) and is recorded in <see cref="Faults"/> with the step that stalled.
    /// </summary>
    internal sealed class FakeSmtpServer : IDisposable
    {
        private readonly TcpListener _listener;
        private readonly CancellationTokenSource _cts = new();
        private readonly ConcurrentQueue<(string User, string Password)> _credentials = new();
        private readonly ConcurrentQueue<string> _faults = new();
        private readonly ConcurrentDictionary<TcpClient, byte> _clients = new();
        private int _connections;

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

        /// <summary>Stalled exchanges, e.g. "FakeSmtpServer: timed out after 10s waiting for the exchange after EHLO; connection closed."</summary>
        public IReadOnlyList<string> Faults => _faults.ToList();

        /// <summary>A failure reason for assertions: the recorded stalls, or a note that there were none.</summary>
        public string Describe() =>
            _faults.IsEmpty ? $"FakeSmtpServer saw {Connections} connection(s) and no stalled exchange" : string.Join(" ", _faults);

        private async Task AcceptLoopAsync()
        {
            while (!_cts.IsCancellationRequested)
            {
                TcpClient client;
                try
                {
                    client = await _listener.AcceptTcpClientAsync(_cts.Token);
                }
                catch (Exception)
                {
                    return;
                }

                Interlocked.Increment(ref _connections);
                client.ReceiveTimeout = (int)IoTimeout.TotalMilliseconds;
                client.SendTimeout = (int)IoTimeout.TotalMilliseconds;
                _clients.TryAdd(client, 0);
                _ = Task.Run(() => ServeAsync(client));
            }
        }

        private async Task ServeAsync(TcpClient client)
        {
            var step = "the greeting";
            using (client)
            {
                try
                {
                    var stream = client.GetStream();
                    using var reader = new StreamReader(stream, Encoding.ASCII);
                    await using var writer = new StreamWriter(stream, Encoding.ASCII) { NewLine = "\r\n", AutoFlush = true };
                    var io = new Io(reader, writer, IoTimeout, _cts.Token);

                    await io.WriteLineAsync("220 fake.smtp ESMTP");
                    step = "the first command";
                    string? line;
                    while ((line = await io.ReadLineAsync()) != null)
                    {
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
                                while ((line = await io.ReadLineAsync()) != null && line != ".")
                                {
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
                }
                catch (TimeoutException)
                {
                    _faults.Enqueue($"FakeSmtpServer: timed out after {IoTimeout.TotalSeconds:0.###}s waiting for {step}; connection closed.");
                }
                catch (Exception) when (_cts.IsCancellationRequested)
                {
                    // Disposed mid-exchange.
                }
                catch (Exception)
                {
                    // The client went away; nothing to record.
                }
                finally
                {
                    _clients.TryRemove(client, out _);
                }
            }
        }

        private async Task AuthenticateAsync(string line, Io io)
        {
            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var mechanism = parts.Length > 1 ? parts[1].ToUpperInvariant() : string.Empty;

            if (mechanism == "PLAIN")
            {
                var payload = parts.Length > 2 ? parts[2] : null;
                if (payload is null)
                {
                    await io.WriteLineAsync("334 ");
                    payload = await io.ReadLineAsync() ?? string.Empty;
                }

                // authzid \0 authcid \0 password
                var fields = Decode(payload).Split('\0');
                _credentials.Enqueue((fields.ElementAtOrDefault(1) ?? string.Empty, fields.ElementAtOrDefault(2) ?? string.Empty));
            }
            else if (mechanism == "LOGIN")
            {
                string user;
                if (parts.Length > 2)
                {
                    user = Decode(parts[2]);
                }
                else
                {
                    await io.WriteLineAsync("334 VXNlcm5hbWU6");
                    user = Decode(await io.ReadLineAsync() ?? string.Empty);
                }

                await io.WriteLineAsync("334 UGFzc3dvcmQ6");
                var password = Decode(await io.ReadLineAsync() ?? string.Empty);
                _credentials.Enqueue((user, password));
            }
            else
            {
                await io.WriteLineAsync("504 Unrecognized authentication type");
                return;
            }

            await io.WriteLineAsync("235 Authentication successful");
        }

        private static string Decode(string base64) => Encoding.UTF8.GetString(Convert.FromBase64String(base64.Trim()));

        public void Dispose()
        {
            _cts.Cancel();
            _listener.Stop();
            _listener.Server.Dispose();
            foreach (var client in _clients.Keys)
            {
                client.Dispose();
            }

            _clients.Clear();
            _cts.Dispose();
        }

        /// <summary>Line I/O where every read and write must finish within the server's IoTimeout.</summary>
        private sealed class Io
        {
            private readonly StreamReader _reader;
            private readonly StreamWriter _writer;
            private readonly TimeSpan _timeout;
            private readonly CancellationToken _stopping;

            public Io(StreamReader reader, StreamWriter writer, TimeSpan timeout, CancellationToken stopping)
            {
                _reader = reader;
                _writer = writer;
                _timeout = timeout;
                _stopping = stopping;
            }

            public Task<string?> ReadLineAsync() => Bounded(ct => _reader.ReadLineAsync(ct).AsTask());

            public Task WriteLineAsync(string line) => Bounded(async ct =>
            {
                await _writer.WriteLineAsync(line.AsMemory(), ct);
                return true;
            });

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
