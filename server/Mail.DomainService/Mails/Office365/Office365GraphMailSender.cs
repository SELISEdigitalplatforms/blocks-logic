using Blocks.Secrets;
using Mail.DomainService.Entities;
using Microsoft.Extensions.Logging;
using Microsoft.Graph.Models;
using Microsoft.Graph.Models.ODataErrors;
using Microsoft.Kiota.Abstractions;
using MimeKit;

namespace Mail.DomainService.Mails.Office365
{
    /// <summary>
    /// Sends an OAuth record through Microsoft Graph as the configured mailbox.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Graph rather than SMTP XOAUTH2 because SMTP AUTH is the part of Exchange Online that fails
    /// in ways a tenant cannot see: it is off by default, can be turned off per mailbox, is blocked
    /// by Security Defaults, and needs an Exchange service principal that nothing in Entra shows.
    /// Graph needs the <c>Mail.Send</c> application permission and admin consent, and nothing else.
    /// </para>
    /// <para>
    /// Two paths, chosen by size. A message that fits in one request goes through
    /// <c>sendMail</c>. A larger one — Graph refuses any request over 4 MB, and the attachment
    /// limit here is well above that — is built as a draft, its attachments added one at a time
    /// (the big ones through an upload session) and then sent. That path also needs
    /// <c>Mail.ReadWrite</c>, because a draft is a message in the mailbox; without it, only mail
    /// with large attachments fails, and it fails as <see cref="Office365FailureCode.PermissionDenied"/>.
    /// </para>
    /// <para>
    /// Reached only through <see cref="Office365MailSender"/>, which has already validated the
    /// record. Every failure path emits exactly one classified error, and nothing here resends.
    /// </para>
    /// </remarks>
    public class Office365GraphMailSender
    {
        public const string GraphHost = "graph.microsoft.com";
        public const int GraphPort = 443;

        /// <summary>The named <see cref="HttpClient"/> the Graph session sends through.</summary>
        public const string HttpClientName = "Office365Graph";

        /// <summary>
        /// Below this, an attachment is posted inline; at or above it, it goes through an upload
        /// session. Microsoft documents 3 MB as the boundary in both directions — an upload session
        /// refuses anything smaller.
        /// </summary>
        internal const long UploadSessionThresholdBytes = 3 * 1024 * 1024;

        /// <summary>
        /// The largest estimated <c>sendMail</c> request, in bytes. Graph refuses a request over
        /// 4 MB; the decimal reading is the conservative one.
        /// </summary>
        internal const long InlineRequestLimitBytes = 4_000_000;

        /// <summary>
        /// What the estimate allows for everything it does not count: JSON structure, recipients,
        /// the sender and attachment metadata. Exchange caps recipients at a few hundred, which is
        /// well inside this.
        /// </summary>
        private const long EnvelopeAllowanceBytes = 64 * 1024;

        private readonly IOffice365TokenProvider _tokenProvider;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<Office365GraphMailSender> _logger;

        public Office365GraphMailSender(
            IOffice365TokenProvider tokenProvider,
            IHttpClientFactory httpClientFactory,
            ILogger<Office365GraphMailSender> logger)
        {
            _tokenProvider = tokenProvider;
            _httpClientFactory = httpClientFactory;
            _logger = logger;
        }

        protected virtual IOffice365GraphMailSession CreateSession(string accessToken) =>
            new GraphSdkMailSession(_httpClientFactory.CreateClient(HttpClientName), accessToken);

        public virtual async Task<bool> SendAsync(MailToBeSent mailToBeSent, MailBody mailBody, string blocksTenantId)
        {
            var configuration = mailToBeSent.MailServerConfiguration;

            string accessToken;
            try
            {
                accessToken = await _tokenProvider.GetTokenAsync(
                    new Office365TokenRequest(
                        blocksTenantId,
                        configuration.TenantId!,
                        configuration.ClientId!,
                        configuration.ClientSecretReference!,
                        Office365TokenScopes.Graph))
                    .ConfigureAwait(false);
            }
            catch (SecretVaultException ex)
            {
                LogFailure(Office365FailureCode.VaultUnavailable, mailToBeSent, ex);
                return false;
            }
            catch (SecretException ex)
            {
                LogFailure(Office365FailureCode.SecretResolutionFailed, mailToBeSent, ex);
                return false;
            }
            catch (Exception ex)
            {
                LogFailure(Office365FailureCode.TokenAcquisitionFailed, mailToBeSent, ex);
                return false;
            }

            var mailbox = configuration.MailboxAddress!;
            var inline = FitsInOneRequest(mailBody);

            Message message;
            try
            {
                message = BuildMessage(mailToBeSent, mailBody, includeAttachments: inline);
            }
            catch (ParseException ex)
            {
                // Before any request. The SMTP path would have thrown the same parse out of the
                // composer; here it is one classified line instead.
                LogFailure(Office365FailureCode.RecipientRejected, mailToBeSent, ex, "an address could not be parsed");
                return false;
            }

            var session = CreateSession(accessToken);

            _logger.LogInformation(
                "Graph (Office 365): sending itemId={ItemId} via {Path} with {AttachmentCount} attachment(s)",
                mailToBeSent.ItemId,
                inline ? "sendMail" : "draft",
                mailBody.Attachments.Count);

            return inline
                ? await SendInlineAsync(session, mailbox, message, mailToBeSent).ConfigureAwait(false)
                : await SendThroughDraftAsync(session, mailbox, message, mailBody, mailToBeSent).ConfigureAwait(false);
        }

        private async Task<bool> SendInlineAsync(
            IOffice365GraphMailSession session,
            string mailbox,
            Message message,
            MailToBeSent mailToBeSent)
        {
            try
            {
                await session.SendMailAsync(mailbox, message).ConfigureAwait(false);
                return true;
            }
            catch (Exception ex)
            {
                LogGraphFailure(ex, mailToBeSent);
                return false;
            }
        }

        private async Task<bool> SendThroughDraftAsync(
            IOffice365GraphMailSession session,
            string mailbox,
            Message message,
            MailBody mailBody,
            MailToBeSent mailToBeSent)
        {
            string draftId;
            try
            {
                draftId = await session.CreateDraftAsync(mailbox, message).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                LogGraphFailure(ex, mailToBeSent);
                return false;
            }

            try
            {
                foreach (var attachment in mailBody.Attachments)
                {
                    if (attachment.Content.LongLength < UploadSessionThresholdBytes)
                    {
                        await session.AddAttachmentAsync(mailbox, draftId, attachment).ConfigureAwait(false);
                    }
                    else
                    {
                        await session.UploadAttachmentAsync(mailbox, draftId, attachment).ConfigureAwait(false);
                    }
                }

                await session.SendDraftAsync(mailbox, draftId).ConfigureAwait(false);

                // Sent. Graph moves the draft to Sent Items itself; there is nothing left to clean up.
                return true;
            }
            catch (Exception ex)
            {
                LogGraphFailure(ex, mailToBeSent);
                await DeleteDraftAsync(session, mailbox, draftId, mailToBeSent).ConfigureAwait(false);
                return false;
            }
        }

        /// <summary>
        /// Removes a draft whose send did not complete, so failed mail does not pile up in Drafts.
        /// </summary>
        /// <remarks>
        /// Best effort, and never a second failure line: the send has already been reported. If the
        /// send in fact went through before its response was lost, the draft is gone already and
        /// this fails harmlessly — it can never unsend anything.
        /// </remarks>
        private async Task DeleteDraftAsync(
            IOffice365GraphMailSession session,
            string mailbox,
            string draftId,
            MailToBeSent mailToBeSent)
        {
            try
            {
                await session.DeleteDraftAsync(mailbox, draftId).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Graph (Office 365): could not delete the unsent draft for itemId={ItemId}. ExceptionType={ExceptionType}",
                    mailToBeSent.ItemId,
                    ex.GetType().Name);
            }
        }

        /// <summary>
        /// Whether the message goes through <c>sendMail</c> in one request.
        /// </summary>
        /// <remarks>
        /// Estimates the request as it goes on the wire rather than the content: attachments travel
        /// as base64, a third larger, and the serializer escapes HTML — every <c>&lt;</c> becomes
        /// six bytes. Always an overestimate, so a borderline message takes the draft path, which
        /// works at any size, rather than a request Graph refuses. Also false when any single
        /// attachment needs an upload session, which only a draft has.
        /// </remarks>
        internal static bool FitsInOneRequest(MailBody mailBody)
        {
            var estimate = EnvelopeAllowanceBytes + JsonEscapedLength(mailBody.Subject) + JsonEscapedLength(mailBody.Body);

            foreach (var attachment in mailBody.Attachments)
            {
                if (attachment.Content.LongLength >= UploadSessionThresholdBytes)
                {
                    return false;
                }

                estimate += (attachment.Content.LongLength + 2) / 3 * 4;
            }

            return estimate <= InlineRequestLimitBytes;
        }

        /// <summary>
        /// An upper bound on a string's length once the Graph serializer has escaped it.
        /// </summary>
        /// <remarks>
        /// The serializer uses the default JSON encoder: printable ASCII passes through, quote and
        /// backslash take two bytes, and HTML-sensitive characters, control characters and
        /// everything outside ASCII become a six-byte <c>\uXXXX</c>.
        /// </remarks>
        private static long JsonEscapedLength(string? value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return 0;
            }

            long length = 0;
            foreach (var c in value)
            {
                length += c switch
                {
                    '"' or '\\' => 2,
                    '<' or '>' or '&' or '\'' or '+' or '`' => 6,
                    < ' ' or > '~' => 6,
                    _ => 1
                };
            }

            return length;
        }

        /// <summary>
        /// The Graph message for a mail, with the same identities the SMTP composer would write.
        /// </summary>
        /// <remarks>
        /// Addresses go through MimeKit's parser, the same one <see cref="MailMessageComposer"/>
        /// uses, so a <c>Name &lt;address&gt;</c> recipient means the same thing on both transports.
        /// The mailbox in the URL is who sends; <c>From</c> is who it appears to be from, and a
        /// different address there needs Send As on the mailbox exactly as it did over SMTP.
        /// </remarks>
        internal static Message BuildMessage(MailToBeSent mailToBeSent, MailBody mailBody, bool includeAttachments)
        {
            var configuration = mailToBeSent.MailServerConfiguration;

            var message = new Message
            {
                Subject = mailBody.Subject,
                Body = new ItemBody { ContentType = BodyType.Html, Content = mailBody.Body },
                ToRecipients = Recipients(mailToBeSent.To),
                CcRecipients = Recipients(mailToBeSent.Cc),
                BccRecipients = Recipients(mailToBeSent.Bcc),
                ReplyTo = Recipients(mailToBeSent.ReplyTo)
            };

            // Left unset when blank, so Graph sends as the mailbox itself rather than refusing an
            // empty address.
            if (!string.IsNullOrWhiteSpace(configuration.SenderAddress))
            {
                message.From = new Recipient
                {
                    EmailAddress = new EmailAddress
                    {
                        Address = configuration.SenderAddress,
                        Name = string.IsNullOrWhiteSpace(configuration.SenderName) ? null : configuration.SenderName
                    }
                };
            }

            if (includeAttachments && mailBody.Attachments.Count > 0)
            {
                message.Attachments = mailBody.Attachments
                    .Select(attachment => (Attachment)new FileAttachment
                    {
                        Name = attachment.FileName,
                        ContentType = attachment.ContentType,
                        ContentBytes = attachment.Content
                    })
                    .ToList();
            }

            return message;
        }

        private static List<Recipient> Recipients(IEnumerable<string>? addresses) =>
            addresses?
                .Select(MailboxAddress.Parse)
                .Select(parsed => new Recipient
                {
                    EmailAddress = new EmailAddress
                    {
                        Address = parsed.Address,
                        Name = string.IsNullOrWhiteSpace(parsed.Name) ? null : parsed.Name
                    }
                })
                .ToList()
            ?? [];

        /// <summary>
        /// Maps a Graph failure onto a code.
        /// </summary>
        /// <remarks>
        /// The Graph error code first, then the HTTP status, and anything ambiguous falls to
        /// <see cref="Office365FailureCode.GraphFailed"/>. Unlike an SMTP reply, the error code is a
        /// documented identifier rather than prose, so it is safe to branch on; the message text
        /// beside it never is, and is never read.
        /// </remarks>
        internal static string Classify(Exception exception) => exception switch
        {
            ODataError odata => Classify(odata.ResponseStatusCode, odata.Error?.Code),
            ApiException api => Classify(api.ResponseStatusCode, null),
            HttpRequestException { InnerException: System.Security.Authentication.AuthenticationException }
                => Office365FailureCode.TlsFailed,
            _ => Office365FailureCode.GraphFailed
        };

        internal static string Classify(int status, string? code) => code switch
        {
            "ErrorSendAsDenied" => Office365FailureCode.SendAsDenied,
            "ErrorInvalidRecipients" => Office365FailureCode.RecipientRejected,
            "ErrorMessageSizeExceeded" => Office365FailureCode.MessageTooLarge,
            "ErrorInvalidUser" or "ErrorNonExistentMailbox" or "MailboxNotEnabledForRESTAPI"
                => Office365FailureCode.MailboxNotFound,
            "ErrorAccessDenied" => Office365FailureCode.PermissionDenied,
            "ErrorExceededMessageLimit" or "ApplicationThrottled" or "ErrorServerBusy"
                => Office365FailureCode.Throttled,
            _ => status switch
            {
                401 => Office365FailureCode.AuthenticationFailed,
                403 => Office365FailureCode.PermissionDenied,
                404 => Office365FailureCode.MailboxNotFound,
                413 => Office365FailureCode.MessageTooLarge,
                429 or 503 => Office365FailureCode.Throttled,
                _ => Office365FailureCode.GraphFailed
            }
        };

        /// <summary>
        /// The status and error code Graph returned, for the log's Reason field.
        /// </summary>
        /// <remarks>
        /// The two things an operator needs to tell a missing permission from a missing mailbox,
        /// and nothing else. The code is reduced to identifier characters and bounded, so a
        /// response cannot write arbitrary text into the log.
        /// </remarks>
        internal static string? Describe(Exception exception) => exception switch
        {
            ODataError odata => $"graph status {odata.ResponseStatusCode} code {SafeCode(odata.Error?.Code)}",
            ApiException api => $"graph status {api.ResponseStatusCode}",
            _ => null
        };

        private static string SafeCode(string? code)
        {
            if (string.IsNullOrEmpty(code))
            {
                return "none";
            }

            var safe = new string(code.Where(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '.').Take(64).ToArray());
            return safe.Length == 0 ? "none" : safe;
        }

        private void LogGraphFailure(Exception exception, MailToBeSent mailToBeSent) =>
            LogFailure(Classify(exception), mailToBeSent, exception, Describe(exception));

        private void LogFailure(string code, MailToBeSent mailToBeSent, Exception exception, string? reason = null) =>
            Office365FailureLog.Write(_logger, code, mailToBeSent, GraphHost, GraphPort, exception, reason);
    }
}
