using System.Diagnostics;
using Blocks.Genesis;
using FluentAssertions;
using FluentValidation;
using FluentValidation.Results;
using Mail.DomainService.Entities;
using Mail.DomainService.Mails;
using Mail.DomainService.Services;
using Microsoft.Extensions.Logging.Abstractions;
using MongoDB.Bson;
using MongoDB.Driver;
using MongoDB.Driver.Core.Events;
using Moq;
using Xunit.Abstractions;
using XUnitTest.Workflow;
using EmailTemplate = Blocks.Genesis.Entities.Os.EmailTemplate;

namespace XUnitTest.Mail
{
    /// <summary>
    /// SPEC10 §5 local harness (C12 proxy; the dev p95 is the author's post-merge check). The §5 workload, 1,000
    /// templates and 50 configurations in a local MongoDB, then 20 warm-up and 200 measured
    /// <c>ProcessMailAsync</c> calls for one existing template. A validator that stops right after mapping keeps
    /// sending out of the timing, so each call is MapAsync plus the recipient lookup. It uses only APIs that also
    /// exist before the change, so the same file measures the base commit and the candidate.
    /// Run with: BLOCKS_PERF_TESTS=1 dotnet test server/XUnitTest/XUnitTest.csproj -c Release
    /// --filter FullyQualifiedName~MailMappingPerformanceTests --logger "console;verbosity=detailed"
    /// (optional BLOCKS_MAIL_PERF_OUT=&lt;file&gt; appends one line per scenario).
    /// </summary>
    public sealed class MailMappingPerformanceTests(ITestOutputHelper output) : IDisposable
    {
        private const int Templates = 1_000;
        private const int Configurations = 50;
        private const int WarmUp = 20;
        private const int Measured = 200;

        private readonly string _databaseName = "blocks_mail_perf_" + Guid.NewGuid().ToString("N");
        private MongoClient? _client;
        private int _reads;

        public void Dispose() => _client?.DropDatabase(_databaseName);

        [PerformanceFact]
        public async Task MapAsync_P95_AndSelectionReads()
        {
            var url = Environment.GetEnvironmentVariable("BLOCKS_FUNCTIONS_TEST_MONGO") ?? "mongodb://localhost:27017";
            var settings = MongoClientSettings.FromConnectionString(url);
            settings.ClusterConfigurator = cluster => cluster.Subscribe<CommandStartedEvent>(e =>
            {
                if (e.DatabaseNamespace?.DatabaseName == _databaseName && e.CommandName is "find" or "aggregate"
                    && e.Command.GetValue(e.CommandName, BsonNull.Value) is { IsString: true } c
                    && c.AsString is "EmailTemplates" or "MailServerConfigurations")
                {
                    Interlocked.Increment(ref _reads);
                }
            });
            _client = new MongoClient(settings);
            var database = _client.GetDatabase(_databaseName);
            Seed(database);

            var provider = new Mock<IDbContextProvider>();
            provider.Setup(p => p.GetCollection<EmailTemplate>("EmailTemplates")).Returns(database.GetCollection<EmailTemplate>("EmailTemplates"));
            provider.Setup(p => p.GetCollection<MailServerConfiguration>("MailServerConfigurations"))
                .Returns(database.GetCollection<MailServerConfiguration>("MailServerConfigurations"));
            provider.Setup(p => p.GetCollection<BsonDocument>("Users")).Returns(database.GetCollection<BsonDocument>("Users"));
            var repository = new MailRepository(provider.Object, new XUnitTest.TestDoubles.PassThroughLogicDataAccess());

            var validator = new Mock<IValidator<MailToBeSent>>();
            validator.Setup(v => v.ValidateAsync(It.IsAny<MailToBeSent>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ValidationResult([new ValidationFailure("stop", "after mapping")]));
            var service = new MailService(validator.Object, Mock.Of<IMessageClient>(), repository, Mock.Of<ISendMailService>(),
                NullLogger<MailService>.Instance);

            // An organization with its own copy (the multi-org case) and the Default organization (single-org).
            foreach (var organization in new[] { "org-1", "default" })
            {
                BlocksContext.SetContext(BlocksContext.Create("t1", [], "caller", true, "/api/mail", organization, DateTime.UtcNow.AddMinutes(5),
                    "user@app.test", [], "caller", "", "Caller", "token", "t1"));
                var request = new SendMail { Purpose = "Purpose-0042", Language = "en-US", To = ["user@app.test"] };

                for (var i = 0; i < WarmUp; i++) await service.ProcessMailAsync(request);

                _reads = 0;
                var timings = new List<double>(Measured);
                for (var i = 0; i < Measured; i++)
                {
                    var watch = Stopwatch.StartNew();
                    await service.ProcessMailAsync(request);
                    timings.Add(watch.Elapsed.TotalMilliseconds);
                }

                timings.Should().HaveCount(Measured).And.OnlyContain(t => t > 0);
                timings.Sort();
                var p95 = timings[(int)Math.Ceiling(0.95 * timings.Count) - 1];
                var line = $"organization={organization} runs={Measured} p50={timings[timings.Count / 2]:F3}ms p95={p95:F3}ms " +
                           $"selectionReadsPerMail={(double)_reads / Measured:F2}";
                output.WriteLine(line);
                var file = Environment.GetEnvironmentVariable("BLOCKS_MAIL_PERF_OUT");
                if (!string.IsNullOrEmpty(file)) await File.AppendAllTextAsync(file, line + Environment.NewLine);
            }

            BlocksContext.ClearContext();
        }

        // 1,000 templates: 450 purposes in the Default organization plus a copy of each in org-1 (900), and
        // 100 in other organizations; 50 configurations, one project default and one default per organization.
        private static void Seed(IMongoDatabase database)
        {
            var templates = new List<BsonDocument>(Templates);
            for (var i = 0; i < 450; i++)
            {
                templates.Add(Template($"tpl-def-{i:D4}", $"Purpose-{i:D4}", "default", "cfg-00"));
                templates.Add(Template($"tpl-org1-{i:D4}", $"Purpose-{i:D4}", "org-1", ""));
            }
            for (var i = 0; i < Templates - 900; i++)
            {
                templates.Add(Template($"tpl-other-{i:D4}", $"Other-{i:D4}", $"org-{2 + i % 10}", "cfg-01"));
            }
            database.GetCollection<BsonDocument>("EmailTemplates").InsertMany(templates);

            var configurations = Enumerable.Range(0, Configurations).Select(i => new BsonDocument
            {
                { "_id", $"cfg-{i:D2}" }, { "Name", $"cfg-{i:D2}" }, { "Host", "smtp.test" }, { "Port", 587 },
                { "OrganizationId", i == 0 ? "default" : $"org-{i % 12}" },
                { "IsDefault", i == 0 }, { "IsOrganizationDefault", i is > 0 and <= 12 }, { "IsInbound", i >= 45 }
            });
            database.GetCollection<BsonDocument>("MailServerConfigurations").InsertMany(configurations);
            database.GetCollection<BsonDocument>("Users").InsertOne(new BsonDocument { { "_id", "u1" }, { "Email", "user@app.test" } });
        }

        private static BsonDocument Template(string id, string name, string organizationId, string configurationId) => new()
        {
            { "_id", id }, { "Name", name }, { "Language", "en-US" }, { "OrganizationId", organizationId },
            { "MailConfigurationId", configurationId }, { "TemplateBody", "<p>{{name}}</p>" }, { "TemplateSubject", "Subject" }
        };
    }
}
