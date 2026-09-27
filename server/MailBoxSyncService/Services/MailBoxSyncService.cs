using Blocks.Genesis;
using Mail.DomainService.Entities;
using Mail.DomainService.Mails;
using Mail.DomainService.Shared.Enums;
using Mail.DomainService.Utilities;
using MailBoxSyncService.Entities;
using MailKit.Security;
using Microsoft.Extensions.Logging.Abstractions;
using MimeKit;
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

        private readonly ILogger<MailBoxSyncService> _logger;

        public MailBoxSyncService(
            IMailRepository repository,
            IMessageClient messageClient,
            IImapClientFactory imapClientFactory,
            ILogger<MailBoxSyncService>? logger = null)
        {
            _repository = repository;
            _messageClient = messageClient;
            _imapClientFactory = imapClientFactory;
            _logger = logger ?? NullLogger<MailBoxSyncService>.Instance;
            _mapClientMapper = new ConcurrentDictionary<string, IImapClientWrapper>();
        }

        public Task SyncInboxAsync(MailServerConfiguration config, string tenantId) =>
            SyncInboxCoreAsync(config, tenantId, saslMechanism: null);

        public Task SyncInboxAsync(MailServerConfiguration config, string tenantId, SaslMechanism saslMechanism)
        {
            ArgumentNullException.ThrowIfNull(saslMechanism);
            return SyncInboxCoreAsync(config, tenantId, saslMechanism);
        }

        private async Task SyncInboxCoreAsync(MailServerConfiguration config, string tenantId, SaslMechanism? saslMechanism)
        {
            var client = await GetOrReconnectImapClientAsync(config, tenantId, saslMechanism);

            var inbox = client.Inbox;
            await inbox.OpenAsync(MailKit.FolderAccess.ReadOnly);

            for (int i = 0; i < inbox.Count; i++)
            {
                var message = await inbox.GetMessageAsync(i);
                await StoreInboundAsync(config, tenantId, message);
            }
        }

        public async Task<bool> StoreInboundAsync(MailServerConfiguration config, string tenantId, MimeMessage message)
        {
            if (string.IsNullOrWhiteSpace(message.MessageId))
                return false;

            if (await _repository.ExistsAsync(message.MessageId, tenantId))
                return false;

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

            try
            {
                await EnqueueInboxEmailInsertionMessage(entity, tenantId);
            }
            catch (Exception ex)
            {
                // The mail is already stored, so a failed trigger must not stop the rest of
                // the inbox from syncing: one bad message would otherwise block every message
                // after it on every poll.
                _logger.LogError(
                    ex,
                    "Mail {MessageId} was saved but its email trigger could not be published for tenant '{TenantId}' using config '{ConfigId}'",
                    entity.MessageId,
                    tenantId,
                    config.ItemId);
            }

            return true;
        }

        /// <summary>
        /// Upper bound on the body carried by a trigger event. Service Bus caps a message at
        /// 256 KB on the Standard tier; UTF-8 can take three bytes per character, so this keeps
        /// the body under ~180 KB with room for the rest of the envelope.
        /// </summary>
        internal const int MaxTriggerBodyLength = 60_000;

        /// <summary>
        /// The mail as a trigger event carries it: everything except the raw MIME, and the body
        /// capped.
        /// </summary>
        /// <remarks>
        /// The raw MIME holds every attachment base64-encoded, so a single attached file used to
        /// push the event past the broker's size limit and the trigger was lost. It stays in the
        /// stored <see cref="MailBoxEntity"/>, which a consumer can load by <c>ItemId</c>.
        /// </remarks>
        internal static MailBoxEntity ForTrigger(MailBoxEntity entity) => new()
        {
            ItemId = entity.ItemId,
            MessageId = entity.MessageId,
            MailServerConfigurationId = entity.MailServerConfigurationId,
            Subject = entity.Subject,
            From = entity.From,
            To = entity.To,
            Date = entity.Date,
            Body = entity.Body is { Length: > MaxTriggerBodyLength } body
                ? body[..MaxTriggerBodyLength]
                : entity.Body,
            Status = entity.Status,
            Error = entity.Error,
            IsInbound = entity.IsInbound,
            RawMime = null!
        };

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
                    Mail = ForTrigger(entity)
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

        private async Task<IImapClientWrapper> GetOrReconnectImapClientAsync(
            MailServerConfiguration config,
            string tenantId,
            SaslMechanism? saslMechanism)
        {
            var key = ConnectionKey(config, tenantId);

            if (_mapClientMapper.TryGetValue(key, out var client))
            {
                if (client.IsConnected && client.IsAuthenticated)
                    return client;

                CleanupClient(key, client);
            }

            return await ConnectImapClientAsync(config, key, saslMechanism);
        }

        /// <summary>
        /// The record's explicit security mode when it has one; otherwise the legacy
        /// <c>EnableSSL</c> reading, which is what every record had before the mode existed.
        /// </summary>
        internal static SecureSocketOptions SocketOptionsFor(MailServerConfiguration config) => config.SecurityMode switch
        {
            MailSecurityMode.SslOnConnect => SecureSocketOptions.SslOnConnect,
            MailSecurityMode.StartTls => SecureSocketOptions.StartTls,
            MailSecurityMode.None => SecureSocketOptions.None,
            _ => config.EnableSSL ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTls
        };

        private async Task<IImapClientWrapper> ConnectImapClientAsync(
            MailServerConfiguration config,
            string key,
            SaslMechanism? saslMechanism)
        {
            var client = _imapClientFactory.Create();

            try
            {
                await client.ConnectAsync(config.Host, config.Port, SocketOptionsFor(config));

                if (saslMechanism is not null)
                {
                    await client.AuthenticateAsync(saslMechanism);
                }
                else
                {
                    await client.AuthenticateAsync(
                        config.SenderUserName,
                        config.AccountPassword);
                }

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
