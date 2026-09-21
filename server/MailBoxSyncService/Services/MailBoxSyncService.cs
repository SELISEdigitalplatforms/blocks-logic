using Blocks.Genesis;
using Mail.DomainService.Entities;
using Mail.DomainService.Mails;
using Mail.DomainService.Shared.Enums;
using Mail.DomainService.Utilities;
using MailBoxSyncService.Entities;
using MailKit.Security;

namespace MailBoxSyncService.Services
{
    public class MailBoxSyncService : IMailBoxSyncService
    {
        private readonly IMailRepository _repository;
        private readonly IMessageClient _messageClient;
        private readonly IImapClientFactory _imapClientFactory;
        private readonly IDictionary<string, IImapClientWrapper> _mapClientMapper;

        public MailBoxSyncService(IMailRepository repository, IMessageClient messageClient, IImapClientFactory imapClientFactory)
        {
            _repository = repository;
            _messageClient = messageClient;
            _imapClientFactory = imapClientFactory;
            _mapClientMapper = new Dictionary<string, IImapClientWrapper>();
        }

        public async Task SyncInboxAsync(MailServerConfiguration config, string tenantId)
        {
            var client = await GetOrReconnectImapClientAsync(config);

            var inbox = client.Inbox;
            await inbox.OpenAsync(MailKit.FolderAccess.ReadOnly);

            for (int i = 0; i < inbox.Count; i++)
            {
                var message = await inbox.GetMessageAsync(i);

                if (string.IsNullOrWhiteSpace(message.MessageId))
                    continue;

                if (await _repository.ExistsAsync(message.MessageId, tenantId))
                    continue;

                var entity = new MailBoxEntity
                {
                    ItemId = Guid.NewGuid().ToString(),
                    MessageId = message.MessageId,
                    MailServerConfigurationId = config.ItemId,
                    Subject = message.Subject ?? "",
                    From = message.From.FirstOrDefault()?.ToString() ?? "",
                    To = string.Join(",", message.To),
                    Date = message.Date.UtcDateTime,
                    RawMime = message.ToString(),
                    Body = message.TextBody,
                    Status = MailStatus.Received,
                    IsInbound = true
                };

                await _repository.InsertAsync(entity, tenantId);
                await EnqueueInboxEmailInsertionMessage(entity, tenantId);
            }
        }

        private async Task EnqueueInboxEmailInsertionMessage(MailBoxEntity entity, string tenantId)
        {
            var securityData = BlocksContext.Create(tenantId, [], "", false, "", "", DateTime.MinValue, "", [], "", "", "", "", "", tenantId);
            BlocksContext.SetContext(securityData, false);

            await _messageClient.SendToConsumerAsync(new ConsumerMessage<EmailTriggerEvent>
            {
                ConsumerName = CommunicationConstants.EmailTriggerQueueName,
                Payload = new EmailTriggerEvent
                {
                    Type = EmailTriggerType.Inbound,
                    Mail = entity
                }
            });
        }

        private async Task<IImapClientWrapper> GetOrReconnectImapClientAsync(MailServerConfiguration config)
        {
            if (_mapClientMapper.TryGetValue(config.SenderUserName, out var client))
            {
                if (client.IsConnected && client.IsAuthenticated)
                    return client;

                CleanupClient(config.SenderUserName, client);
            }

            return await ConnectImapClientAsync(config);
        }

        private async Task<IImapClientWrapper> ConnectImapClientAsync(MailServerConfiguration config)
        {
            var client = _imapClientFactory.Create();

            try
            {
                await client.ConnectAsync(
                    config.Host,
                    config.Port,
                    config.EnableSSL
                        ? SecureSocketOptions.SslOnConnect
                        : SecureSocketOptions.StartTls);

                await client.AuthenticateAsync(
                    config.SenderUserName,
                    config.AccountPassword);

                _mapClientMapper[config.SenderUserName] = client;
                return client;
            }
            catch
            {
                client.Dispose();
                throw;
            }
        }

        private void CleanupClient(string key, IImapClientWrapper client)
        {
            try
            {
                client.Dispose();
            }
            catch
            {
                // swallow – we're cleaning up anyway
            }
            finally
            {
                _mapClientMapper.Remove(key);
            }
        }

        public async Task SyncOutgoingAsync(SesEventNotification? sesEvent, string tenantId)
        {
            if (sesEvent?.Mail?.MessageId == null)
                return;

            foreach (var recipient in sesEvent.Mail.Destination ?? [])
            {
                var subject = sesEvent.Mail?.Headers?.FirstOrDefault(h => h.Name.Equals("Subject", StringComparison.OrdinalIgnoreCase))?.Value;
                var body = sesEvent.Mail?.Headers?.FirstOrDefault(h => h.Name.Equals("X-Mail-Body", StringComparison.OrdinalIgnoreCase))?.Value;

                var entity = new MailBoxEntity
                {
                    ItemId = Guid.NewGuid().ToString(),
                    MessageId = sesEvent.Mail!.MessageId,
                    From = sesEvent.Mail.Source,
                    To = recipient,
                    Date = sesEvent.Mail.Timestamp,
                    Status = MapStatus(sesEvent),
                    Error = ExtractError(sesEvent),
                    Subject = subject,
                    Body = body,
                    IsInbound = false
                };

                await _repository.InsertAsync(entity, tenantId);
            }
        }

        private static MailStatus MapStatus(SesEventNotification ses)
        {
            return ses.EventType switch
            {
                "Send" => MailStatus.Sent,
                "Delivery" => MailStatus.Delivered,
                "Bounce" => MailStatus.Bounced,
                "Complaint" => MailStatus.Complained,
                "Reject" => MailStatus.Rejected,
                _ => MailStatus.Unknown
            };
        }

        private static string? ExtractError(SesEventNotification ses)
        {
            if (ses.Bounce?.BouncedRecipients?.Any() == true)
                return ses.Bounce.BouncedRecipients[0].DiagnosticCode;

            return null;
        }
    }
}
