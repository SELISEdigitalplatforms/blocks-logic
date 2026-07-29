using Blocks.Genesis;
using Dtos = DomainService.Dtos;
//using DomainService.Dtos;
//using DomainService.Migration;
//using DomainService.Projects;
//using DomainService.Shared;
//using DomainService.Shared.Dtos;
//using DomainService.Shared.Entities;
//using DomainService.Utilities;
//using DomainService.Worker;
using DomainService.Workflow;
using DomainService.Workflow.Events;
using DomainService.Workflow.Nodes.TriggerDataV1;
using DomainService.Workflow.Utils;
using DomainService.Shared;
using Iam.DomainService.Utilities;
using Mail.DomainService.Dtos;
using Mail.DomainService.Mails;
using Mail.DomainService.Shared.Utilities;
using SeliseBlocks.ConfigurationDriver;
using Worker;
using Worker.Configuration;
using Worker.Consumers.Mail;
using Worker.Consumers.Workflow;
using Worker.Consumers;

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

            //services.RegisterAllServices();
            services.AddSingleton<ISendMailService, SendMailService>();
            services.AddSingleton<SmtpClientProvider>();
            services.AddSingleton<MicrosoftSmtpClient>();
            services.AddSingleton<MailKitSmtpClient>();
            services.RegisterAllMailApplicationServices();

            services.AddWorkflowExecutionEngine();
            services.AddSingleton<IConsumer<AddExcuationNodeEvent>, AddExcuationNodeConsumer>();
            //services.AddSingleton<IConsumer<EmailTriggerEvent>, EmailTriggerConsumer>();
            services.AddSingleton<IConsumer<DataChangeEvent>, DataTriggerConsumer>();
            services.AddSingleton<IConsumer<Dtos.MigrationCompletionEvent>, MigrationCompletionConsumer>();
             services.AddApplicationServices();
             services.RegisterSharedServices();

             ApplicationConfigurations.ConfigureWorker(services, LogicConstants.GetMessageConfiguration(secret.MessageConnectionString));
        });
