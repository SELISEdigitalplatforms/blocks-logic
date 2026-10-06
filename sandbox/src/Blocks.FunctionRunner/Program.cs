using Blocks.FunctionRunner.Utils;
using Blocks.Genesis;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

// Blocks.FunctionRunner — the Genesis worker that executes tenant functions in gVisor
// sandboxes on this VM.
//
// The bootstrap mirrors every other Blocks worker (DECISIONS D10) so the runner boots, logs,
// reports health and resolves configuration the same way: vault type, IBlocksSecret,
// ICacheClient for Redis, Genesis Serilog and OpenTelemetry.
//
// The runner returns results over Redis and the control-plane Worker is the single Mongo
// writer (D1). It does read one tenant-scoped thing itself: the values behind a run's
// {{secret.<id>}} references, resolved right before the sandbox starts so the queue never holds
// plaintext (SecretStore/BlocksSecretsRunResolver). That goes through SeliseBlocks.Secrets.OS —
// secret metadata on the tenant's own Mongo connection, found through ITenants, and values in
// Key Vault (KeyVault__* in runner.env) — read-only, and nothing is ever written back.

const string serviceName = "blocks-function-runner";

var builder = Host.CreateApplicationBuilder(args);

// Genesis .env and command-line handling for workers.
ApplicationConfigurations.ConfigureWorkerEnv(builder.Configuration, args);

// BLOCKS_VAULT_TYPE selects where bootstrap secrets come from: OnPrem (1) reads them from
// the environment, which is what /etc/blocks-runner/runner.env provides; Azure (2) uses Key
// Vault. OnPrem is the default so a VM without Key Vault still starts.
var vaultType = ApplicationConfigurations.ResolveVaultType(VaultType.OnPrem);

// Serilog with the platform enrichers and the Mongo LMT sink, plus the bootstrap secret.
var secret = await ApplicationConfigurations.ConfigureLogAndSecretsAsync(serviceName, vaultType);

// Genesis worker wiring: ICacheClient, ITenants, OpenTelemetry, health checks.
// The message connection is deliberately allowed to be empty — the runner speaks Redis
// Streams, not the service bus, so it must not require a broker to start.
// Before Genesis sets up tracing, so it runs ahead of Genesis's exporter (see TraceTagSanitizer).
Blocks.FunctionRunner.Tracing.TraceTagSanitizer.AddFirst(builder.Services);
ApplicationConfigurations.ConfigureWorker(builder.Services, new MessageConfiguration
{
    ServiceName = serviceName,
    Connection = secret.MessageConnectionString ?? string.Empty,
});

builder.Services.AddFunctionRunnerServices(builder.Configuration);
builder.Services.AddFunctionRunnerWorkerServices();

// systemd integration: Type=notify, so the unit is only "active" once the host is up.
builder.Services.AddSystemd();
// A stopping runner lets the runs it started finish: the longest a run can take is the startup
// allowance + timeout + kill grace (30 + 30 + 2 s), and systemd allows 90 s (TimeoutStopSec).
// The .NET default of 30 s cut runs off and they were executed again elsewhere (FN-3).
builder.Services.Configure<HostOptions>(o => o.ShutdownTimeout = TimeSpan.FromSeconds(80));

var host = builder.Build();
await host.RunAsync();
