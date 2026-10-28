using Blocks.Extension.DependencyInjection;
using DataAccess.InternalService;
using Blocks.Genesis;
using Blocks.Genesis.Notifications;
using DomainService.Notification.Push;
using Blocks.Secrets;
using DomainService.Shared;
using DomainService.Projects;
using Proxy.DomainService;
using Workflow.DomainService;
using Workflow.DomainService.Events;
using Workflow.DomainService.Import;
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
        .ConfigureServices((hostContext, services) =>
        {
            services.AddHttpClient();

            services.Configure<VerioSystemSettings>(services.BuildServiceProvider().GetRequiredService<IConfiguration>().GetSection("VerioSystemSettings"));
            services.AddSingleton<IConsumer<SendMail>, SendConsumer>();
            services.AddHostedService<PeriodicPingBackgroundService>();

            services.AddSingleton<ISendMailService, SendMailService>();
            services.AddSingleton<SmtpClientProvider>();
            services.AddSingleton<MicrosoftSmtpClient>();
            services.AddSingleton<MailKitSmtpClient>();

            // Outbound senders are registered here. AddApplicationServices calls this again;
            // the sender registrations are idempotent so that second call does not add another
            // AmazonSes (or any other provider) to IEnumerable<IOutboundMailSender>.
            services.RegisterAllMailApplicationServices();

            services.AddSingleton<IWorkflowImportTenantSlugResolver>(sp =>
            {
                var repo = sp.GetService<IProjectRepository>();
                return repo is null
                    ? new NullWorkflowImportTenantSlugResolver()
                    : new ProjectTenantSlugResolver(repo);
            });
            services.AddWorkflowExecutionEngine();
            // The Proxy action node resolves IProxyGatewayService, and node executors are
            // constructed here in the worker, not in the API host. AddBlocksSecrets supplies the
            // ISecretService that ProxyVariableResolver needs to expand {{$VAR.name}} tokens in a
            // proxy's configured headers and query values; without it any proxy that stores its
            // upstream credential as a configuration variable fails at forward time.
            services.AddBlocksSecrets();
            services.AddLogicDataAccess();
            services.AddProxyServices();
            services.AddSingleton<Workflow.DomainService.Utils.NodeQueueLanes>(_ => new Workflow.DomainService.Utils.NodeQueueLanes());
            services.AddSingleton<IConsumer<AddExcuationNodeEvent>, AddExcuationNodeConsumer>();
            services.AddSingleton<IConsumer<WorkflowImportEvent>, WorkflowImportConsumer>();
            services.AddSingleton<IConsumer<DataChangeEvent>, DataTriggerConsumer>();
            services.AddSingleton<IConsumer<EmailTriggerEvent>, EmailTriggerConsumer>();
            // The event trigger: starts the functions that subscribe to a FunctionEvent (ctx.events.add('FunctionEvent', ...)).
            services.AddSingleton<IConsumer<Functions.DomainService.Events.FunctionEvent>, Functions.DomainService.Consumers.FunctionEventConsumer>();
            services.AddSingleton<IConsumer<PublishScheduleCommand>, SchedulerTriggerConsumer>();
            // Mobile push: genesis senders queue PushRequested; this worker renders and sends it through FCM.
            services.AddSingleton<IConsumer<PushRequested>, PushRequestedConsumer>();
            services.AddBlocksNotifications();
            services.AddApplicationServices();
            // IScheduleService only (WorkflowService needs it). Hangfire and the schedule registry run in Scheduler.Worker.
            services.AddSchedulerServices();
            services.AddFunctionsServices(hostContext.Configuration);
            services.AddFunctionsWorkerServices();
            services.AddSingleton<Workflow.DomainService.Nodes.INodeExecutor, Functions.DomainService.Nodes.ActionFunctionNode>();
            services.AddStorageDomainServices();
            services.RegisterBlocksStorageServices();
            //services.RegisterSharedServices();

            // Before Genesis sets up tracing, so it runs ahead of Genesis's exporter (see TraceTagSanitizer).
            Common.InternalService.Tracing.TraceTagSanitizer.AddFirst(services);
            ApplicationConfigurations.ConfigureWorker(services, LogicConstants.GetWorkerMessageConfiguration(secret.MessageConnectionString));
        });

