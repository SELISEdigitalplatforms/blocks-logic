using Blocks.Extension.DependencyInjection;
using Blocks.Genesis;
using Blocks.Secrets;
using DomainService.Shared;
using Proxy.DomainService;
using Workflow.DomainService;
using Workflow.DomainService.Events;
using Workflow.DomainService.Nodes.TriggerDataV1;
using Workflow.DomainService.Utils;
using Mail.DomainService.Dtos;
using Mail.DomainService.Mails;
using Mail.DomainService.Shared.Utilities;
using Functions.DomainService.Utils;
using Scheduler.DomainService.Models;
using Scheduler.DomainService.Utils;
using SeliseBlocks.ConfigurationDriver;
using Storage.DomainService.Utilities;
using Worker;
using Worker.Configuration;
using Worker.Consumers;
using Worker.Consumers.Mail;
using Worker.Consumers.Workflow;
using Dtos = DomainService.Dtos;

const string _serviceName = "blocks-logic-worker";
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
        .ConfigureServices((services) =>
        {
            services.AddHttpClient();

            services.Configure<VerioSystemSettings>(services.BuildServiceProvider().GetRequiredService<IConfiguration>().GetSection("VerioSystemSettings"));
            services.AddSingleton<IConsumer<SendEmailEvent>, SendEmailConsumer>();
            services.AddSingleton<IConsumer<SendMail>, SendConsumer>();
            services.AddHostedService<PeriodicPingBackgroundService>();

            services.AddSingleton<ISendMailService, SendMailService>();
            services.AddSingleton<SmtpClientProvider>();
            services.AddSingleton<MicrosoftSmtpClient>();
            services.AddSingleton<MailKitSmtpClient>();
            services.RegisterAllMailApplicationServices();

            services.AddWorkflowExecutionEngine();
            // The Proxy action node resolves IProxyGatewayService, and node executors are
            // constructed here in the worker, not in the API host. AddBlocksSecrets supplies the
            // ISecretService that ProxyVariableResolver needs to expand {{$VAR.name}} tokens in a
            // proxy's configured headers and query values; without it any proxy that stores its
            // upstream credential as a configuration variable fails at forward time.
            services.AddBlocksSecrets();
            services.AddProxyServices();
            services.AddSingleton<IConsumer<AddExcuationNodeEvent>, AddExcuationNodeConsumer>();
            services.AddSingleton<IConsumer<DataChangeEvent>, DataTriggerConsumer>();
            services.AddSingleton<IConsumer<EmailTriggerEvent>, EmailTriggerConsumer>();
            services.AddSingleton<IConsumer<PublishScheduleCommand>, SchedulerTriggerConsumer>();
            services.AddApplicationServices();
            services.AddSchedulerServices();
            services.AddSchedulerWorkerServices();
            services.AddFunctionsServices();
            services.AddFunctionsWorkerServices();
            services.AddSingleton<Workflow.DomainService.Nodes.INodeExecutor, Functions.DomainService.Nodes.ActionFunctionNode>();
            services.AddStorageDomainServices();
            services.RegisterBlocksStorageServices();
            //services.RegisterSharedServices();

            ApplicationConfigurations.ConfigureWorker(services, LogicConstants.GetMessageConfiguration(secret.MessageConnectionString));
        });

