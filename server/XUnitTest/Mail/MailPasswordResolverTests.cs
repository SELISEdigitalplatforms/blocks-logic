using System.Collections.Concurrent;
using System.Reflection;
using System.Security.Claims;
using Blocks.Genesis;
using Blocks.Secrets;
using DomainService.ManagedService.Services;
using FluentAssertions;
using Mail.DomainService.Entities;
using Mail.DomainService.Mails;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using XUnitTest.TestHelpers;

namespace XUnitTest.Mail
{
    /// <summary>
    /// The shared mail password resolver (#371): reference-first resolution, the tenant context of
    /// the read, the restore of the caller's context and the failure classes.
    /// </summary>
    public class MailPasswordResolverTests
    {
        private const string Reference = "secret-ref-7f3a";
        private const string SecretPassword = "new-pass-9c1e";
        private const string Tenant = "tenant-a";

        private static MailServerConfiguration Referenced(string reference = Reference, string accountPassword = "") =>
            new() { ItemId = "cfg-1", AccountPassword = accountPassword, AccountPasswordReference = reference };

        private static BlocksContext CallerContext(string tenantId = "caller-tenant") =>
            BlocksContext.Create(tenantId, [], "caller-user", true, "/api/mail", "org", DateTime.UtcNow.AddMinutes(5),
                "caller@test.com", [], "caller", "", "Caller", "token", tenantId);

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        public async Task LegacyConfig_ReturnsThePlaintext_WithoutCreatingAScope(string? reference)
        {
            // H5, C10: no reference, no DI scope and no secret read.
            var scopes = new Mock<IServiceScopeFactory>(MockBehavior.Strict);
            var sut = new MailPasswordResolver(scopes.Object);

            var result = await sut.ResolveAsync(
                new MailServerConfiguration { AccountPassword = "app-pass-1", AccountPasswordReference = reference }, Tenant);

            result.Succeeded.Should().BeTrue();
            result.Password.Should().Be("app-pass-1");
            scopes.Verify(s => s.CreateScope(), Times.Never);
        }

        [Fact]
        public async Task ReferencedConfig_ReadsInsideAnAuthenticatedContextOfTheGivenTenant_AndRestoresTheCaller()
        {
            // H6: the read runs as the send's tenant even though another authenticated caller
            // context is forced in place; that context is back afterwards.
            var seen = new List<BlocksContext?>();
            var secrets = MailSecretsTestDoubles.Answering(Reference, SecretPassword, seen);
            var caller = CallerContext();
            BlocksContext.SetContext(caller, true);

            try
            {
                var result = await MailSecretsTestDoubles.PasswordResolver(secrets).ResolveAsync(Referenced(), Tenant);

                result.Succeeded.Should().BeTrue();
                result.Password.Should().Be(SecretPassword);
                seen.Should().ContainSingle();
                seen[0]!.TenantId.Should().Be(Tenant);
                seen[0]!.IsAuthenticated.Should().BeTrue();
                secrets.Verify(s => s.GetValueAsync(Reference, It.IsAny<CancellationToken>()), Times.Once);
                BlocksContext.GetContext().Should().BeSameAs(caller);
            }
            finally
            {
                BlocksContext.SetContext(null!, false);
            }
        }

        [Fact]
        public async Task UnderAnAmbientHttpIdentityOfAnotherTenant_ReadsAsTheGivenTenant_AndRestoresTheCaller()
        {
            // H6, C5: an API request carries an authenticated HttpContext identity, which
            // BlocksContext.GetContext() prefers over a context set with changeContext: false.
            // Only changeContext: true makes the read run in the record's tenant.
            var seen = new List<BlocksContext?>();
            var sut = MailSecretsTestDoubles.PasswordResolver(MailSecretsTestDoubles.Answering(Reference, SecretPassword, seen));

            var previousAccessor = BlocksHttpContextAccessor.Instance;
            var accessor = new HttpContextAccessor();
            BlocksHttpContextAccessor.Instance = accessor;
            try
            {
                accessor.HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                        new[] { new Claim("tenant_id", "caller-tenant"), new Claim("user_id", "caller-user"), new Claim("sub", "caller-user") },
                        "Bearer")),
                };
                BlocksContext.GetContext()!.TenantId.Should().Be("caller-tenant", "precondition: the ambient HTTP identity is the caller context");

                var result = await Task.Run(() => sut.ResolveAsync(Referenced(), Tenant));

                result.Password.Should().Be(SecretPassword);
                seen.Should().ContainSingle();
                seen[0]!.TenantId.Should().Be(Tenant);
                BlocksContext.GetContext()!.TenantId.Should().Be("caller-tenant");
            }
            finally
            {
                accessor.HttpContext = null;
                BlocksHttpContextAccessor.Instance = previousAccessor;
                BlocksContext.SetContext(null!, false);
            }
        }

        // C6 harness. In an ordinary flow the runtime rolls back AsyncLocal changes made inside an
        // awaited method, so a missing `finally` would go unnoticed. Here, on a pool thread with no
        // SynchronizationContext, the flow is suppressed and the pending read is completed on that
        // same thread: the resolver's code after its await then runs inline in this thread's
        // ExecutionContext with no rollback, so the context left here is exactly what the explicit
        // restore wrote. `completingContext` is put on the thread first, so the result differs from
        // the caller's context whenever no restore happens.
        internal static async Task<(BlocksContext? Left, T Result)> ContextLeftAfterReadAsync<T>(
            BlocksContext? caller,
            BlocksContext completingContext,
            Action<TaskCompletionSource<string>> completeRead,
            Func<IServiceScopeFactory, Task<T>> read)
        {
            var pendingRead = new TaskCompletionSource<string>(); // inline continuations on purpose
            var secrets = new Mock<ISecretService>(MockBehavior.Strict);
            secrets.Setup(s => s.GetValueAsync(Reference, It.IsAny<CancellationToken>())).Returns(pendingRead.Task);
            var scopes = MailSecretsTestDoubles.ScopeFactory(secrets);

            var (left, resolve) = await Task.Run(() =>
            {
                BlocksContext.SetContext(caller!, caller is not null);
                var flow = ExecutionContext.SuppressFlow();
                try
                {
                    var pending = read(scopes);
                    pending.IsCompleted.Should().BeFalse("the secret read must still be pending");

                    BlocksContext.SetContext(completingContext, true);
                    completeRead(pendingRead);
                    pending.IsCompleted.Should().BeTrue("the resolver must have finished inline on this thread");

                    return (BlocksContext.GetContext(), pending);
                }
                finally
                {
                    flow.Undo();
                }
            });

            return (left, await resolve);
        }

        internal static Action<TaskCompletionSource<string>> CompleteRead(string outcome) => outcome switch
        {
            "success" => read => read.SetResult(SecretPassword),
            nameof(SecretNotFoundException) => read => read.SetException(new SecretNotFoundException(Reference)),
            _ => read => read.SetException(new InvalidOperationException("store misconfigured")),
        };

        [Theory]
        [InlineData("success")]
        [InlineData(nameof(SecretNotFoundException))]
        [InlineData(nameof(InvalidOperationException))]
        public async Task AfterTheRead_ExplicitlyRestoresTheCallerContext(string outcome)
        {
            // C6: whether the read succeeds or throws, the resolver writes the caller's context back.
            var caller = CallerContext();
            try
            {
                var (left, result) = await ContextLeftAfterReadAsync(
                    caller, CallerContext("completing-tenant"), CompleteRead(outcome),
                    scopes => new MailPasswordResolver(scopes).ResolveAsync(Referenced(), Tenant));

                left.Should().BeSameAs(caller);
                result.Succeeded.Should().Be(outcome == "success");
            }
            finally
            {
                BlocksContext.SetContext(null!, false);
            }
        }

        [Theory]
        [InlineData("success")]
        [InlineData(nameof(SecretNotFoundException))]
        public async Task WithNoCallerContext_ExplicitlyLeavesNoContextBehind(string outcome)
        {
            // C6: a background caller (queue consumer, inbox poller) had no context and gets none back.
            try
            {
                var (left, result) = await ContextLeftAfterReadAsync(
                    null, CallerContext("completing-tenant"), CompleteRead(outcome),
                    scopes => new MailPasswordResolver(scopes).ResolveAsync(Referenced(), Tenant));

                left.Should().BeNull();
                result.Succeeded.Should().Be(outcome == "success");
            }
            finally
            {
                BlocksContext.SetContext(null!, false);
            }
        }

        [Theory]
        [InlineData("success")]
        [InlineData(nameof(SecretNotFoundException))]
        public async Task ServiceBusResolver_AfterTheRead_ExplicitlyRestoresTheCallerContext(string outcome)
        {
            // C6 for the connection string read, which shares the same reader.
            var caller = CallerContext();
            try
            {
                var (left, error) = await ContextLeftAfterReadAsync(
                    caller, CallerContext("completing-tenant"), CompleteRead(outcome),
                    async scopes =>
                    {
                        try
                        {
                            await new ServiceBusConnectionStringResolver(scopes).ResolveAsync(Tenant, Reference);
                            return (Exception?)null;
                        }
                        catch (Exception ex)
                        {
                            return ex;
                        }
                    });

                left.Should().BeSameAs(caller);
                (error is null).Should().Be(outcome == "success");
            }
            finally
            {
                BlocksContext.SetContext(null!, false);
            }
        }

        [Fact]
        public async Task CreatesAndDisposesAFreshScopePerCall_AndCachesNothing()
        {
            // C10, A4: ISecretService is scoped; the singleton must not capture one, and every
            // resolution is a new read.
            var secrets = new Mock<ISecretService>();
            secrets.Setup(s => s.GetValueAsync(Reference, It.IsAny<CancellationToken>())).ReturnsAsync(SecretPassword);
            var provider = new Mock<IServiceProvider>();
            provider.Setup(p => p.GetService(typeof(ISecretService))).Returns(secrets.Object);
            var scope = new Mock<IServiceScope>();
            scope.SetupGet(s => s.ServiceProvider).Returns(provider.Object);
            var scopes = new Mock<IServiceScopeFactory>();
            scopes.Setup(s => s.CreateScope()).Returns(scope.Object);
            var sut = new MailPasswordResolver(scopes.Object);

            await sut.ResolveAsync(Referenced(), Tenant);
            await sut.ResolveAsync(Referenced(), Tenant);

            scopes.Verify(s => s.CreateScope(), Times.Exactly(2));
            scope.Verify(s => s.Dispose(), Times.Exactly(2));
            secrets.Verify(s => s.GetValueAsync(Reference, It.IsAny<CancellationToken>()), Times.Exactly(2));
        }

        [Theory]
        [InlineData(nameof(SecretNotFoundException), "credential")]
        [InlineData(nameof(SecretStateException), "credential")]
        [InlineData(nameof(SecretAccessDeniedException), "credential")]
        [InlineData(nameof(SecretVaultException), "vault_unavailable")]
        [InlineData(nameof(SecretValidationException), "unexpected")]
        [InlineData(nameof(InvalidOperationException), "unexpected")]
        [InlineData(nameof(TimeoutException), "unexpected")]
        public async Task MapsFailuresToAFailureClass_WithoutFallingBackToThePlaintext(string failure, string failureClass)
        {
            // C1, C2, C3, Q4: no plaintext fallback, only the exception type is surfaced.
            var secrets = MailSecretsTestDoubles.Throwing(Reference, MailSecretsTestDoubles.SecretFailure(failure, Reference));

            var result = await MailSecretsTestDoubles.PasswordResolver(secrets).ResolveAsync(Referenced(accountPassword: "old-pass"), Tenant);

            result.Succeeded.Should().BeFalse();
            result.Password.Should().BeNull();
            result.FailureClass.Should().Be(failureClass);
            result.ExceptionType.Should().Be(failure);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("  ")]
        public async Task ReferencedConfig_WithoutATenant_FailsWithoutASecretRead(string? tenantId)
        {
            // C5: no tenant may stand in for the record's own.
            var scopes = new Mock<IServiceScopeFactory>(MockBehavior.Strict);

            var result = await new MailPasswordResolver(scopes.Object).ResolveAsync(Referenced(accountPassword: "old-pass"), tenantId);

            result.Succeeded.Should().BeFalse();
            result.Password.Should().BeNull();
            result.FailureClass.Should().Be("unexpected");
            scopes.Verify(s => s.CreateScope(), Times.Never);
        }

        [Fact]
        public async Task SecretServiceNotRegistered_FailsAsUnexpected()
        {
            // Q4: a host without AddBlocksSecrets() fails the send instead of throwing.
            using var root = new ServiceCollection().BuildServiceProvider();
            var sut = new MailPasswordResolver(root.GetRequiredService<IServiceScopeFactory>());

            var result = await sut.ResolveAsync(Referenced(), Tenant);

            result.Succeeded.Should().BeFalse();
            result.FailureClass.Should().Be("unexpected");
            result.ExceptionType.Should().Be(nameof(InvalidOperationException));
        }

        [Fact]
        public async Task TwoTenantsConcurrently_EachReadsInItsOwnTenant()
        {
            // C5: two sends for different tenants in flight at once never cross.
            var gateA = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            var gateB = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            using var bothStarted = new CountdownEvent(2);
            var tenants = new ConcurrentDictionary<string, string>();
            var secrets = new Mock<ISecretService>(MockBehavior.Strict);
            secrets.Setup(s => s.GetValueAsync("ref-a", It.IsAny<CancellationToken>()))
                   .Returns(() => { tenants["ref-a"] = BlocksContext.GetContext()!.TenantId; bothStarted.Signal(); return gateA.Task; });
            secrets.Setup(s => s.GetValueAsync("ref-b", It.IsAny<CancellationToken>()))
                   .Returns(() => { tenants["ref-b"] = BlocksContext.GetContext()!.TenantId; bothStarted.Signal(); return gateB.Task; });
            var sut = MailSecretsTestDoubles.PasswordResolver(secrets);

            var a = Task.Run(() => { BlocksContext.SetContext(CallerContext("tenant-a"), true); return sut.ResolveAsync(Referenced("ref-a"), "tenant-a"); });
            var b = Task.Run(() => { BlocksContext.SetContext(CallerContext("tenant-b"), true); return sut.ResolveAsync(Referenced("ref-b"), "tenant-b"); });
            bothStarted.Wait(TimeSpan.FromSeconds(10)).Should().BeTrue("both reads must be in flight together");
            gateB.SetResult("pass-b");
            gateA.SetResult("pass-a");

            (await a).Password.Should().Be("pass-a");
            (await b).Password.Should().Be("pass-b");
            tenants["ref-a"].Should().Be("tenant-a");
            tenants["ref-b"].Should().Be("tenant-b");
        }

        [Theory]
        [InlineData(typeof(MailPasswordResolver))]
        [InlineData(typeof(ServiceBusConnectionStringResolver))]
        public void Singletons_HoldNoMutableState(Type singleton)
        {
            // C4, C5: no credential (or any other) field a concurrent call could overwrite or keep.
            singleton.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                     .Should().OnlyContain(f => f.IsInitOnly);
        }

        [Fact]
        public async Task Result_DoesNotRenderThePassword()
        {
            // C4: a result that ends up in a log or exception text must not carry the value.
            var result = await MailSecretsTestDoubles.PasswordResolver(MailSecretsTestDoubles.Answering(Reference, SecretPassword))
                .ResolveAsync(Referenced(), Tenant);

            result.ToString().Should().NotContain(SecretPassword);
        }

        [Fact]
        public void CredentialException_CarriesOnlyTheClassAndTheType()
        {
            // C4, Q3: what the inbound worker logs has no inner exception and no reference id.
            var ex = new MailCredentialException("credential", nameof(SecretNotFoundException));

            ex.InnerException.Should().BeNull();
            ex.FailureClass.Should().Be("credential");
            ex.ToString().Should().Contain("credential").And.Contain(nameof(SecretNotFoundException)).And.NotContain(Reference);
        }
    }
}
