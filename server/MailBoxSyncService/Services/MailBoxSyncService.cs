using Blocks.Genesis;
using Mail.DomainService.Entities;
using Mail.DomainService.Mails;
using Mail.DomainService.Shared.Enums;
using Mail.DomainService.Utilities;
using MailBoxSyncService.Entities;
using MailKit.Security;
using System.Collections.Concurrent;

namespace MailBoxSyncService.Services
{
    public class MailBoxSyncService : IMailBoxSyncService
    {
        private readonly IMailRepository _repository;
        private readonly IMessageClient _messageClient;
        private readonly IImapClientFactory _imapClientFactory;

        /// <summary>
        /// Live IMAP sessions, keyed by tenant, configuration and provider.
        /// </summary>
        /// <remarks>
        /// The key used to be the username alone. This service is a singleton, so two tenants that
        /// happened to configure the same mailbox username shared one authenticated session and
        /// each read the other's inbox — the username is not an identity here, it is a field two
        /// unrelated records can hold the same value in. Concurrent because the map outlives any
        /// single sync and nothing guarantees two configurations are never processed at once.
        /// </remarks>
        private readonly ConcurrentDictionary<string, IImapClientWrapper> _mapClientMapper;

        public MailBoxSyncService(IMailRepository repository, IMessageClient messageClient, IImapClientFactory imapClientFactory)
        {
            _repository = repository;
            _messageClient = messageClient;
            _imapClientFactory = imapClientFactory;
            _mapClientMapper = new ConcurrentDictionary<string, IImapClientWrapper>();
        }

        public async Task SyncInboxAsync(MailServerConfiguration config, string tenantId)
        {
            var client = await GetOrReconnectImapClientAsync(config, tenantId);

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

        /// <summary>
        /// The connection-cache key: the tenant, the configuration and the provider together.
        /// </summary>
        /// <remarks>
        /// All three, because none of them is unique on its own. Two tenants can hold the same
        /// configuration name and the same mailbox username, and a tenant can hold two
        /// configurations against the same host.
        /// </remarks>
        internal static string ConnectionKey(MailServerConfiguration config, string tenantId) =>
            string.Join('\u001f', tenantId, config.ItemId, (int)config.Provider);

        private async Task<IImapClientWrapper> GetOrReconnectImapClientAsync(MailServerConfiguration config, string tenantId)
        {
            var key = ConnectionKey(config, tenantId);

            if (_mapClientMapper.TryGetValue(key, out var client))
            {
                if (client.IsConnected && client.IsAuthenticated)
                    return client;

                CleanupClient(key, client);
            }

            return await ConnectImapClientAsync(config, key);
        }

        private async Task<IImapClientWrapper> ConnectImapClientAsync(MailServerConfiguration config, string key)
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

                // Replacing an entry disposes whatever it displaced: a reconnect that raced
                // another would otherwise leak the loser's authenticated session.
                if (_mapClientMapper.TryGetValue(key, out var displaced) && !ReferenceEquals(displaced, client))
                {
                    CleanupClient(key, displaced);
                }

                _mapClientMapper[key] = client;
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
                _mapClientMapper.TryRemove(key, out _);
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
