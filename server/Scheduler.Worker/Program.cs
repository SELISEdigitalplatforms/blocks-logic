using Blocks.Genesis;
using DataAccess.InternalService;
using Scheduler.DomainService.Utils;
using SeliseBlocks.ConfigurationDriver;

// Scheduler.Worker: hosts Hangfire (in-memory), the schedule registry consumers and the startup reseed.
// It must run as exactly one replica: every replica would fire every schedule. Host bootstrap only;
// the scheduler code lives in Scheduler.DomainService.
const string _serviceName = "blocks-logic-scheduler-worker";
var vaultType = ApplicationConfigurations.ResolveVaultType();
Console.WriteLine($"Using Genesis vault type: {vaultType}");
var secret = await ApplicationConfigurations.ConfigureLogAndSecretsAsync(_serviceName, vaultType);

await CreateHostBuilder(args).Build().RunAsync();

IHostBuilder CreateHostBuilder(string[] args) =>
        Host.CreateDefaultBuilder(args)
        .ConfigureAppConfiguration((context, builder) =>
        {
            builder.AddMongoDbConfiguration(options =>
            {
                options.ConnectionString = secret.DatabaseConnectionString;
                options.DatabaseName = secret.RootDatabaseName;
                options.CollectionName = "Secrets";
                options.SecretKey = "blocks-secret-logic";
            });
        })
        .ConfigureServices((hostContext, services) =>
        {
            services.AddLogicDataAccess();
            services.AddSchedulerServices();
            services.AddSchedulerWorkerServices();

            // Last: Genesis builds its routing table from the services registered so far.
            ApplicationConfigurations.ConfigureWorker(services, SchedulerConstants.GetSchedulerMessageConfiguration(secret.MessageConnectionString));
        });
