using Blocks.Extensions.DependencyInjection;
using Blocks.Genesis;
using BlocksTemplate.Api;
using SeliseBlocks.ConfigurationDriver;
using Captcha.DomainService.Configuration;
using Cloud.DomainService.Utilities;
using Cloud.LmtService.Utilities;
using CloudConfiguration.DomainService.Shared.Utilities;
using DomainService.Notification;
using DomainService.Shared;
using DomainService.Utilities;
using DomainService.Workflow;
using DomainService.Workflow.Utils;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Driver;
using Path = System.IO.Path;

var serviceName = "blocks-logic";
//var vaultType = ResolveVaultType();
//Console.WriteLine($"Using Genesis vault type: {vaultType}");
var secret = await ApplicationConfigurations.ConfigureLogAndSecretsAsync(serviceName, VaultType.Azure);
var builder = WebApplication.CreateBuilder(args);
ApplicationConfigurations.ConfigureApiEnv(builder, args);

builder.Configuration.AddMongoDbConfiguration(options =>
{
    options.ConnectionString = secret.DatabaseConnectionString;
    options.DatabaseName     = secret.RootDatabaseName;
    options.CollectionName   = "Secrets";
    options.SecretKey        = "blocks-Secret";
});

ApplicationConfigurations.ConfigureServices(builder.Services, LogicConstants.GetMessageConfiguration(secret.MessageConnectionString));

builder.Services.Configure<FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = 15 * 1024 * 1024; // 15 MB
});

var services = builder.Services;

services.AddHealthChecks();

ApplicationConfigurations.ConfigureApi(services, serviceName);


builder.Services.Configure<MvcOptions>(options =>
{
    options.Conventions.Insert(0, new GlobalApiRoutePrefixConvention("api"));
});

var wwwrootPath = Path.Combine(builder.Environment.ContentRootPath, "wwwroot");
Directory.CreateDirectory(wwwrootPath);

services.RegisterAllServices();
services.AddApplicationServices();
services.AddCloudDomainServices();
services.AddCloudLmtServices();
services.AddCloudConfigurationServices();
services.AddWorkflowExecutionEngine();
services.RegisterAllNotificationApplicationServices();
services.RegisterBlocksEurolmServices();
await services.RegisterBlocksDeploymentServicesAsync(VaultType.Azure);
services.RegisterBlocksObservabilityServices();
services.AddBlocksSwagger(new BlocksSwaggerOptions
{
    Title = "Blocks Logic API",
    Version = "v1",
    EnableBearerAuth = true
});

var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();

var indexHtml = Path.Combine(app.Environment.WebRootPath ?? "", "index.html");

if (File.Exists(indexHtml))
{

    app.MapFallback(async context =>
    {
        if (context.Request.Path.StartsWithSegments("/api"))
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            await context.Response.WriteAsJsonAsync(new { message = "Not Found" });
            return;
        }

        var tenantService = context.RequestServices.GetRequiredService<ITenants>();
        var dbContext = context.RequestServices.GetRequiredService<IDbContextProvider>();
        var host = context.Request.Host.Value;
        var tenant = tenantService.GetTenantByApplicationDomain(host);
        var database = dbContext.GetDatabase(tenant.TenantId);
        var captcheSetting = await (await database.GetCollection<CaptchaConfiguration>("CaptchaConfigurations").FindAsync(Builders<CaptchaConfiguration>.Filter.Eq(mc => mc.IsEnable, true))).FirstOrDefaultAsync();
        ApplyFrontendRuntimeSettings(builder.Configuration, wwwrootPath, tenant.TenantId, captcheSetting.CaptchaKey);

        context.Response.Cookies.Append("x-blocks-key", tenant.TenantId, new CookieOptions
        {
            Domain = tenant.Applications.FirstOrDefault()?.CookieDomain,
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.None,
            Path = "/"
        });

        await context.Response.SendFileAsync(indexHtml);

    });
}

//ApplicationConfigurations.ConfigureMiddleware(app);
ApplicationConfigurations.ConfigureMiddleware(app,
    tenantValidationPrefixes: new[] { "api/notificationHub" });
app.MapHub<NotificationHub>("/api/notificationHub").WithDisplayName("Controller/notificationHub"); 
await app.RunAsync();

static void ApplyFrontendRuntimeSettings(IConfiguration configuration, string webRootPath, string blocksKey, string googleSiteKey)
{

    DotNetEnv.Env.Load();

    blocksKey = !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BLOCKS_X_BLOCKS_KEY")) ? Environment.GetEnvironmentVariable("BLOCKS_X_BLOCKS_KEY") : blocksKey;
    googleSiteKey = !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BLOCKS_GOOGLE_SITE_KEY")) ? Environment.GetEnvironmentVariable("BLOCKS_GOOGLE_SITE_KEY") : googleSiteKey;

    var replacements = new Dictionary<string, string?>
    {
        ["__BLOCKS_API_BASE_URL__"] = configuration["LogicBaseUrl"],
        ["__BLOCKS_X_BLOCKS_KEY__"] = blocksKey,
        ["__BLOCKS_GOOGLE_SITE_KEY__"] = googleSiteKey,
        ["__BLOCKS_CONSTRUCT_URL__"] = configuration["ConstructBaseUrl"],
        ["__BLOCKS_UDS_API_BASE_URL__"] = configuration["UdsBaseUrl"],
        ["__BLOCKS_IDP_API_BASE_URL__"] = configuration["IdpBaseUrl"],
        ["__BLOCKS_AGENT_API_BASE_URL__"] = configuration["AgentBaseUrl"],
        ["__BLOCKS_EUROLM_API_BASE_URL__"] = configuration["EurolmBaseUrl"],
        ["__BLOCKS_UTILITY_API_BASE_URL__"] = configuration["UtilityBaseUrl"]
    };

    var files = Directory.EnumerateFiles(webRootPath, "*", SearchOption.AllDirectories)
        .Where(path =>
        {
            var ext = Path.GetExtension(path);
            return ext.Equals(".html", StringComparison.OrdinalIgnoreCase)
                || ext.Equals(".js", StringComparison.OrdinalIgnoreCase)
                || ext.Equals(".css", StringComparison.OrdinalIgnoreCase)
                || ext.Equals(".json", StringComparison.OrdinalIgnoreCase);
        });

    foreach (var filePath in files)
    {
        var content = File.ReadAllText(filePath);
        var updated = content;

        foreach (var (token, value) in replacements)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                updated = updated.Replace(token, value, StringComparison.Ordinal);
            }
        }

        if (!ReferenceEquals(content, updated) && !content.Equals(updated, StringComparison.Ordinal))
        {
            File.WriteAllText(filePath, updated);
        }
    }
}
