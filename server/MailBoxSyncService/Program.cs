using Blocks.Genesis;
using Blocks.Secrets;
using Workflow.DomainService.Utils;
using Mail.DomainService.Mails.Office365;
using Mail.DomainService.Mails.Strategies;
using Mail.DomainService.Shared.Utilities;
using MailBoxSyncService.Services;
using SeliseBlocks.ConfigurationDriver;

const string serviceName = "blocks-logic-mailboxsync";
var vaultType = ApplicationConfigurations.ResolveVaultType();
Console.WriteLine($"Using Genesis vault type: {vaultType}");
var secret = await ApplicationConfigurations.ConfigureLogAndSecretsAsync(serviceName, vaultType);

var builder = WebApplication.CreateBuilder(args);

builder.Configuration.AddMongoDbConfiguration(options =>
{
    options.ConnectionString = secret.DatabaseConnectionString;
    options.DatabaseName = secret.RootDatabaseName;
    options.CollectionName = "Secrets";
    options.SecretKey = "blocks-secret-logic";
});

builder.Services.AddHttpClient();
builder.Services.AddSingleton<IMailBoxSyncService, global::MailBoxSyncService.Services.MailBoxSyncService>();
builder.Services.AddSingleton<IMailRepository, MailRepository>();
builder.Services.AddSingleton<IImapClientFactory, MailKitImapClientFactory>();

// Inbound strategies. A provider with no poller here is refused by the registry before anything
// connects, rather than by a provider check.
builder.Services.AddSingleton<IInboundMailPoller, AmazonSesImapPoller>();
builder.Services.AddSingleton<IInboundMailPoller, ZohoImapPoller>();
builder.Services.AddSingleton<IInboundMailPoller, GmailImapPoller>();
builder.Services.AddSingleton<IInboundMailPoller, Office365GraphInboxPoller>();

// Office 365 inbound reads through Graph with a client-credentials token, whose secret lives in
// Blocks Secrets. The token provider opens its own scope for the scoped ISecretService.
builder.Services.AddBlocksSecrets();
builder.Services.RegisterOffice365TokenServices();
builder.Services.AddHttpClient(Office365GraphInboxSessionFactory.HttpClientName, client => client.Timeout = TimeSpan.FromSeconds(100));
builder.Services.AddSingleton<IOffice365GraphInboxSessionFactory, Office365GraphInboxSessionFactory>();
builder.Services.AddSingleton<IInboundMailPollerRegistry, InboundMailPollerRegistry>();
builder.Services.AddSingleton<ISnsEventProcessor, SnsEventProcessor>();
builder.Services.AddHostedService<global::MailBoxSyncService.Worker>();
builder.Services.AddControllers();

ApplicationConfigurations.ConfigureServices(builder.Services, LogicConstants.GetMessageConfiguration(secret.MessageConnectionString));

var host = builder.Build();
host.MapControllers();
await host.RunAsync();
