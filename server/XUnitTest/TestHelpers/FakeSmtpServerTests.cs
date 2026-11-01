using System.IO;
using System.Net.Sockets;
using System.Text;
using FluentAssertions;

namespace XUnitTest.TestHelpers
{
    /// <summary>
    /// The test SMTP server's own limits: client input is validated and every session is bounded,
    /// so a misbehaving client under test fails with a recorded fault instead of hanging the run.
    /// </summary>
    public class FakeSmtpServerTests
    {
        private static readonly TimeSpan Wait = TimeSpan.FromSeconds(10);

        private static string Base64(string value) => Convert.ToBase64String(Encoding.UTF8.GetBytes(value));

        /// <summary>A raw line-based client for driving the server byte by byte.</summary>
        private sealed class RawClient : IDisposable
        {
            private readonly TcpClient _tcp = new();
            private StreamReader _reader = null!;
            private StreamWriter _writer = null!;

            public static async Task<RawClient> ConnectAsync(FakeSmtpServer server)
            {
                var client = new RawClient();
                using var timeout = new CancellationTokenSource(Wait);
                await client._tcp.ConnectAsync(server.Host, server.Port, timeout.Token);
                var stream = client._tcp.GetStream();
                client._reader = new StreamReader(stream, Encoding.ASCII);
                client._writer = new StreamWriter(stream, Encoding.ASCII) { NewLine = "\r\n", AutoFlush = true };
                (await client.ReadLineAsync()).Should().StartWith("220");
                return client;
            }

            public async Task<string?> ReadLineAsync()
            {
                using var timeout = new CancellationTokenSource(Wait);
                return await _reader.ReadLineAsync(timeout.Token);
            }

            /// <summary>Reads until the server closes the connection; true when it did within the wait.</summary>
            public async Task<bool> ServerClosedAsync()
            {
                try
                {
                    for (var i = 0; i < 10; i++)
                    {
                        if (await ReadLineAsync() is null)
                        {
                            return true;
                        }
                    }

                    return false;
                }
                catch (IOException)
                {
                    return true; // reset by the server while closing
                }
            }

            public Task WriteLineAsync(string line) => _writer.WriteLineAsync(line);

            public async Task WriteAsync(string text) => await _writer.WriteAsync(text);

            public async Task<string?> CommandAsync(string line)
            {
                await WriteLineAsync(line);
                return await ReadLineAsync();
            }

            public void Dispose() => _tcp.Dispose();
        }

        [Fact]
        public async Task MalformedBase64InAuthPlain_IsRecordedAsAFault_AndTheSessionGoesOn()
        {
            using var server = new FakeSmtpServer();
            using var client = await RawClient.ConnectAsync(server);

            (await client.CommandAsync("AUTH PLAIN !!!not-base64!!!")).Should().StartWith("501");
            (await client.CommandAsync("AUTH PLAIN " + Base64("\0user\0pw"))).Should().StartWith("235");

            server.Credentials.Should().Equal(("user", "pw"));
            server.Faults.Should().ContainSingle().Which.Should().Contain("malformed Base64").And.Contain("AUTH PLAIN");
            server.Describe().Should().Contain("malformed Base64");
        }

        [Fact]
        public async Task MalformedBase64InAuthLoginPassword_IsRecordedAsAFault_AndNoCredentialIsKept()
        {
            using var server = new FakeSmtpServer();
            using var client = await RawClient.ConnectAsync(server);

            (await client.CommandAsync("AUTH LOGIN")).Should().Be("334 VXNlcm5hbWU6");
            (await client.CommandAsync(Base64("user"))).Should().Be("334 UGFzc3dvcmQ6");
            (await client.CommandAsync("%%%")).Should().StartWith("501");

            server.Credentials.Should().BeEmpty();
            server.Faults.Should().ContainSingle().Which.Should().Contain("AUTH LOGIN password");
        }

        [Fact]
        public async Task MalformedBase64InAuthLoginUsername_IsRecordedAsAFault()
        {
            using var server = new FakeSmtpServer();
            using var client = await RawClient.ConnectAsync(server);

            (await client.CommandAsync("AUTH LOGIN %%%")).Should().StartWith("501");

            server.Credentials.Should().BeEmpty();
            server.Faults.Should().ContainSingle().Which.Should().Contain("AUTH LOGIN username");
        }

        [Fact]
        public async Task ALineLongerThanTheCap_EndsTheSession_WithAFault()
        {
            using var server = new FakeSmtpServer();
            using var client = await RawClient.ConnectAsync(server);

            // No line terminator: the server must stop buffering at the cap rather than wait for one.
            await client.WriteAsync(new string('A', FakeSmtpServer.MaxLineLength + 100));

            (await client.ServerClosedAsync()).Should().BeTrue();
            server.Faults.Should().ContainSingle().Which.Should().Contain($"longer than {FakeSmtpServer.MaxLineLength}");
        }

        [Fact]
        public async Task ALineAtTheCap_IsAccepted()
        {
            using var server = new FakeSmtpServer();
            using var client = await RawClient.ConnectAsync(server);

            (await client.CommandAsync("NOOP " + new string('A', FakeSmtpServer.MaxLineLength - 5))).Should().Be("250 OK");

            server.Faults.Should().BeEmpty();
        }

        [Fact]
        public async Task TooManyCommandsInOneSession_EndsTheSession_WithAFault()
        {
            using var server = new FakeSmtpServer();
            using var client = await RawClient.ConnectAsync(server);

            for (var i = 0; i < FakeSmtpServer.MaxCommandsPerSession - 1; i++)
            {
                (await client.CommandAsync("NOOP")).Should().Be("250 OK");
            }

            (await client.CommandAsync("NOOP")).Should().Be("250 OK");
            (await client.ReadLineAsync()).Should().StartWith("421");
            (await client.ServerClosedAsync()).Should().BeTrue();
            server.Faults.Should().ContainSingle().Which.Should().Contain($"limit of {FakeSmtpServer.MaxCommandsPerSession} commands");
        }

        [Fact]
        public async Task ATerminatedDataBlock_IsQueued()
        {
            using var server = new FakeSmtpServer();
            using var client = await RawClient.ConnectAsync(server);

            (await client.CommandAsync("DATA")).Should().StartWith("354");
            await client.WriteLineAsync("Subject: hi");
            (await client.CommandAsync(".")).Should().Be("250 OK queued");
            (await client.CommandAsync("QUIT")).Should().Be("221 Bye");

            server.Faults.Should().BeEmpty();
            server.Describe().Should().Contain("1 connection(s)");
        }

        [Fact]
        public async Task AStalledClient_IsTimedOut_WithAFault()
        {
            using var server = new FakeSmtpServer(TimeSpan.FromMilliseconds(200));
            using var client = await RawClient.ConnectAsync(server);

            (await client.ServerClosedAsync()).Should().BeTrue();
            server.Faults.Should().ContainSingle().Which.Should().Contain("timed out", server.Describe()).And.Contain("the first command");
        }

        [Fact]
        public void Dispose_IsIdempotent()
        {
            var server = new FakeSmtpServer();

            server.Dispose();
            var again = () => server.Dispose();

            again.Should().NotThrow();
        }
    }
}
