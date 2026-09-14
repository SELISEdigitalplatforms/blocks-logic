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
// Note it never asks for a tenant database. The runner returns results over Redis and the
// control-plane Worker is the single Mongo writer (D1), so no tenant credential is ever
// resident on this host.

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
ApplicationConfigurations.ConfigureWorker(builder.Services, new MessageConfiguration
{
    ServiceName = serviceName,
    Connection = secret.MessageConnectionString ?? string.Empty,
});

builder.Services.AddFunctionRunnerServices(builder.Configuration);
builder.Services.AddFunctionRunnerWorkerServices();

// systemd integration: Type=notify, so the unit is only "active" once the host is up.
builder.Services.AddSystemd();

var host = builder.Build();
await host.RunAsync();
