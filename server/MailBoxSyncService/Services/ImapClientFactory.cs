using MailKit;
using MailKit.Net.Imap;
using MailKit.Security;
using MimeKit;

namespace MailBoxSyncService.Services
{
    public interface IImapClientFactory
    {
        IImapClientWrapper Create();
    }

    public interface IImapClientWrapper : IDisposable
    {
        bool IsConnected { get; }
        bool IsAuthenticated { get; }
        IImapFolderWrapper Inbox { get; }
        Task ConnectAsync(string host, int port, SecureSocketOptions options);
        Task AuthenticateAsync(string userName, string password);
    }

    public interface IImapFolderWrapper
    {
        int Count { get; }
        Task OpenAsync(FolderAccess access);
        Task<MimeMessage> GetMessageAsync(int index);
    }

    public sealed class MailKitImapClientFactory : IImapClientFactory
    {
        public IImapClientWrapper Create()
        {
            return new MailKitImapClientWrapper(new ImapClient());
        }
    }

    internal sealed class MailKitImapClientWrapper : IImapClientWrapper
    {
        private readonly ImapClient _client;

        public MailKitImapClientWrapper(ImapClient client)
        {
            _client = client;
        }

        public bool IsConnected => _client.IsConnected;
        public bool IsAuthenticated => _client.IsAuthenticated;
        public IImapFolderWrapper Inbox => new MailKitImapFolderWrapper(_client.Inbox);

        public Task ConnectAsync(string host, int port, SecureSocketOptions options) => _client.ConnectAsync(host, port, options);
        public Task AuthenticateAsync(string userName, string password) => _client.AuthenticateAsync(userName, password);
        public void Dispose() => _client.Dispose();
    }

    internal sealed class MailKitImapFolderWrapper : IImapFolderWrapper
    {
        private readonly IMailFolder _folder;

        public MailKitImapFolderWrapper(IMailFolder folder)
        {
            _folder = folder;
        }

        public int Count => _folder.Count;
        public Task OpenAsync(FolderAccess access) => _folder.OpenAsync(access);
        public Task<MimeMessage> GetMessageAsync(int index) => _folder.GetMessageAsync(index);
    }
}
