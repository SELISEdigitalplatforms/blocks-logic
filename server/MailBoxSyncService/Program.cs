using Blocks.Genesis;
using Workflow.DomainService.Utils;
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
builder.Services.AddSingleton<ISnsEventProcessor, SnsEventProcessor>();
builder.Services.AddHostedService<global::MailBoxSyncService.Worker>();
builder.Services.AddControllers();

ApplicationConfigurations.ConfigureServices(builder.Services, LogicConstants.GetMessageConfiguration(secret.MessageConnectionString));

var host = builder.Build();
host.MapControllers();
await host.RunAsync();
