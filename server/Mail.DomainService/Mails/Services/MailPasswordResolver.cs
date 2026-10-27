using Blocks.Secrets;
using Mail.DomainService.Entities;
using Mail.DomainService.Shared.Utilities;
using Microsoft.Extensions.DependencyInjection;

namespace Mail.DomainService.Mails
{
    /// <summary>
    /// Outcome of one mail password resolution. The password lives only in this object for the
    /// duration of one send or connect; ToString is not overridden, so it never prints the value.
    /// </summary>
    public sealed class MailPasswordResult
    {
        /// <summary>The secret is missing, archived, locked or access-denied.</summary>
        public const string CredentialFailure = "credential";

        /// <summary>The secret store could not be reached.</summary>
        public const string VaultUnavailable = "vault_unavailable";

        /// <summary>Anything else: a malformed reference, a missing tenant, a misconfigured host.</summary>
        public const string Unexpected = "unexpected";

        private MailPasswordResult(bool succeeded, string? password, string? failureClass, string? exceptionType)
        {
            Succeeded = succeeded;
            Password = password;
            FailureClass = failureClass;
            ExceptionType = exceptionType;
        }

        public bool Succeeded { get; }
        public string? Password { get; }
        public string? FailureClass { get; }

        /// <summary>The type name of the exception that failed the read; never its message.</summary>
        public string? ExceptionType { get; }

        public static MailPasswordResult Success(string? password) => new(true, password, null, null);

        public static MailPasswordResult Failure(string failureClass, string? exceptionType) =>
            new(false, null, failureClass, exceptionType);
    }

    public interface IMailPasswordResolver
    {
        /// <summary>
        /// The password for one send or one IMAP connect of <paramref name="configuration"/>,
        /// read in <paramref name="tenantId"/> when the record holds a reference.
        /// </summary>
        Task<MailPasswordResult> ResolveAsync(
            MailServerConfiguration configuration,
            string? tenantId,
            CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Resolves the account password of a password-auth mail configuration, shared by outbound
    /// sends and inbound IMAP connects. A non-empty <c>AccountPasswordReference</c> is
    /// authoritative and is read from Blocks Secrets in the record's tenant; otherwise the legacy
    /// plaintext <c>AccountPassword</c> is used with no secret read.
    /// </summary>
    /// <remarks>
    /// Registered as a singleton, so it holds no per-call state: ISecretService is scoped and is
    /// resolved from a fresh DI scope on every call (<see cref="TenantSecretReader"/>), and nothing
    /// is cached across sends.
    /// </remarks>
    public sealed class MailPasswordResolver : IMailPasswordResolver
    {
        private readonly IServiceScopeFactory _scopeFactory;

        public MailPasswordResolver(IServiceScopeFactory scopeFactory)
        {
            _scopeFactory = scopeFactory;
        }

        public async Task<MailPasswordResult> ResolveAsync(
            MailServerConfiguration configuration,
            string? tenantId,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(configuration);

            var reference = configuration.AccountPasswordReference;
            if (string.IsNullOrEmpty(reference))
            {
                return MailPasswordResult.Success(configuration.AccountPassword);
            }

            // The reference wins: AccountPassword is never used from here on, even on failure.
            // Blocks Secrets refuses a blank tenant, and no other tenant may stand in for it.
            if (string.IsNullOrWhiteSpace(tenantId))
            {
                return MailPasswordResult.Failure(MailPasswordResult.Unexpected, null);
            }

            try
            {
                var password = await TenantSecretReader
                    .ReadAsync(_scopeFactory, tenantId, reference, cancellationToken)
                    .ConfigureAwait(false);

                return MailPasswordResult.Success(password);
            }
            // Only the exception type is surfaced: Secret* messages and properties carry the reference id.
            catch (SecretVaultException ex)
            {
                return MailPasswordResult.Failure(MailPasswordResult.VaultUnavailable, ex.GetType().Name);
            }
            catch (Exception ex) when (ex is SecretNotFoundException or SecretStateException or SecretAccessDeniedException)
            {
                return MailPasswordResult.Failure(MailPasswordResult.CredentialFailure, ex.GetType().Name);
            }
            catch (Exception ex)
            {
                // e.g. SecretValidationException for a malformed reference, or
                // InvalidOperationException from a host without AddBlocksSecrets().
                return MailPasswordResult.Failure(MailPasswordResult.Unexpected, ex.GetType().Name);
            }
        }
    }

    /// <summary>
    /// A mail account password could not be resolved. Carries only the failure class and the
    /// original exception's type name: no inner exception, because Secret* messages and properties
    /// carry the reference id, and whoever logs this exception must not log that id.
    /// </summary>
    public sealed class MailCredentialException : Exception
    {
        public MailCredentialException(string failureClass, string? exceptionType)
            : base($"The mail account password could not be resolved (failure class {failureClass}, {exceptionType ?? "no exception"}).")
        {
            FailureClass = failureClass;
            ExceptionType = exceptionType;
        }

        public MailCredentialException()
            : this(MailPasswordResult.Unexpected, (string?)null)
        {
        }

        public MailCredentialException(string message)
            : base(message)
        {
            FailureClass = MailPasswordResult.Unexpected;
        }

        public MailCredentialException(string message, Exception innerException)
            : base(message, innerException)
        {
            FailureClass = MailPasswordResult.Unexpected;
        }

        public string FailureClass { get; }

        public string? ExceptionType { get; }
    }
}
