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

            services.AddSingleton<IConsumer<RefreshTokenEvent>, RefreshTokenWorkerService>();
            services.AddSingleton<IConsumer<UserAuthenticationTimelineEvent>, UserAuthenticationTimelineWorkerService>();
            services.AddSingleton<IConsumer<MfaActionEvent>, UpdateMfaConfigurationService>();

            services.AddSingleton<IConsumer<ResourceMutationEvent>, ResourceMutationConsumer>();
            services.AddSingleton<IConsumer<ResourceSetToPermissionMutationEvent>, ResourceSetToPermissionMutationConsumer>();
            services.AddSingleton<IConsumer<UserMutationEvent>, UserMutationConsumer>();
            services.AddSingleton<IConsumer<AccountActivityEvent>, AccountActivityWorkerService>();
            services.AddSingleton<IConsumer<CreateUserByEmailEvent>, CreateUserByEmailConsumer>();
            services.AddSingleton<IConsumer<CreateUserRequest>, CreateUserConsumer>();
            services.AddSingleton<IConsumer<CreateUserViaSsoEvent>, CreateUserViaSsoConsumer>();
            services.AddSingleton<IConsumer<UserStatusChangedEvent>, UserStatusChangedConsumer>();

            services.AddHostedService<PeriodicPingBackgroundService>();

            services.RegisterAllServices();



            #region Identifier Service Consumers
            services.AddApplicationServices();
            services.AddSingleton<IConsumer<Tenant>, ConfigureProjectConsumer>();
            services.AddSingleton<IConsumer<DisableDomainBindingRequest>, DisableDomainBindingConsumer>();
            services.AddSingleton<IConsumer<RestoreProjectRequest>, RestoreProjectConsumer>();
            services.AddSingleton<IConsumer<CreateUserByEmailPostEvent_Identifier>, CreateUserByEmailPostConsumer>();
            services.AddSingleton<IConsumer<ConfigureDomainRequest>, DomainConfigureConsumer>();
            services.AddSingleton<IConsumer<MigrationCompletionEvent>, MigrationCompletionConsumer>();
            services.AddSingleton<IConsumer<EnvironmentDataMigrationEvent>, EnvironmentDataMigrationEventConsumer>();
            services.AddSingleton<IConsumer<PublishScheduleCommand>, DataCleanupConsumer>();
            services.AddSingleton<IConsumer<UpdateResourceUsageCommand_Identifier>, UpdateResourceUsageConsumer>();



            services.AddWorkflowExecutionEngine();
            services.AddSingleton<IConsumer<AddExcuationNodeEvent>, AddExcuationNodeConsumer>();
            services.AddSingleton<IConsumer<EmailTriggerEvent>, EmailTriggerConsumer>();
            services.AddSingleton<IConsumer<DataChangeEvent>, DataTriggerConsumer>();
            services.AddSingleton<IConsumer<Dtos.MigrationCompletionEvent>, MigrationCompletionConsumer>();
            services.AddApplicationServices();

            ApplicationConfigurations.ConfigureWorker(services, LogicConstants.GetMessageConfiguration(secret.MessageConnectionString));
            //ApplicationConfigurations.ConfigureWorker(services, IdentifierConstants.GetMessageConfiguration(secret.MessageConnectionString));
            #endregion
        });
