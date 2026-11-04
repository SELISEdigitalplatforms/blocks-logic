using Blocks.Genesis;
using FluentAssertions;
using FluentValidation;
using Mail.DomainService.Entities;
using Mail.DomainService.Mails;
using Mail.DomainService.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;
using MongoDB.Driver.Core.Events;
using Moq;
using EmailTemplate = Blocks.Genesis.Entities.Os.EmailTemplate;

namespace XUnitTest.Mail
{
    /// <summary>
    /// SPEC10 (logic#406): which template and configuration a send uses, against a real MongoDB holding the
    /// §7 data set. Documents are inserted as raw BSON so a template without <c>OrganizationId</c> (H9) and a
    /// configuration without <c>IsOrganizationDefault</c> (C11) are stored exactly as old documents are.
    /// Every test also counts the template and configuration reads (H10: at most 2 per mapped mail).
    /// Needs a local MongoDB: <c>BLOCKS_FUNCTIONS_TEST_MONGO</c> (default <c>mongodb://localhost:27017</c>), as
    /// the other repository tests; each run uses, and drops, its own database.
    /// </summary>
    public sealed class MailSendSelectionMongoTests : IDisposable
    {
        private const string Purpose = "OrderConfirmation";
        private const string Language = "en-US";
        private const string Recipient = "user@app.test";
        private const string TemplateMissingMessage = "The purpose and language combination does not exist. Please check the EmailTemplates collection";
        private const string ConfigurationMissingMessage = "The purpose and language combination does not have a registered email server configuration. Please check the MailServerConfiguration collection";

        private readonly MongoClient _client;
        private readonly string _databaseName = "blocks_mail_select_" + Guid.NewGuid().ToString("N");
        private readonly IMongoDatabase _database;
        private readonly Mock<ISendMailService> _sender = new();
        private readonly RecordingLogger _logger = new();
        private readonly MailService _service;
        private int _selectionReads;
        private MailToBeSent? _sent;

        public MailSendSelectionMongoTests()
        {
            var url = Environment.GetEnvironmentVariable("BLOCKS_FUNCTIONS_TEST_MONGO") ?? "mongodb://localhost:27017";
            var settings = MongoClientSettings.FromConnectionString(url);
            settings.ClusterConfigurator = cluster => cluster.Subscribe<CommandStartedEvent>(CountSelectionRead);
            _client = new MongoClient(settings);
            _database = _client.GetDatabase(_databaseName);

            var provider = new Mock<IDbContextProvider>();
            provider.Setup(p => p.GetCollection<EmailTemplate>("EmailTemplates")).Returns(_database.GetCollection<EmailTemplate>("EmailTemplates"));
            provider.Setup(p => p.GetCollection<MailServerConfiguration>("MailServerConfigurations"))
                .Returns(_database.GetCollection<MailServerConfiguration>("MailServerConfigurations"));
            provider.Setup(p => p.GetCollection<BsonDocument>("Users")).Returns(_database.GetCollection<BsonDocument>("Users"));
            var repository = new MailRepository(provider.Object, new XUnitTest.TestDoubles.PassThroughLogicDataAccess());

            var validator = new EmailValidator(new CommonEmailValidator(repository),
                Options.Create(new MailAttachmentOptions { ValidateAttachmentsExist = false }));
            _sender.Setup(s => s.ProcessSendMailAsync(It.IsAny<MailToBeSent>()))
                .Callback<MailToBeSent>(m => _sent = m)
                .ReturnsAsync(true);
            _service = new MailService(validator, Mock.Of<IMessageClient>(), repository, _sender.Object, _logger);

            Users().InsertOne(new BsonDocument { { "_id", "u1" }, { "Email", Recipient } });
            SeedSection7();
        }

        public void Dispose()
        {
            BlocksContext.ClearContext();
            _client.DropDatabase(_databaseName);
        }

        private void CountSelectionRead(CommandStartedEvent e)
        {
            if (e.DatabaseNamespace?.DatabaseName != _databaseName || (e.CommandName != "find" && e.CommandName != "aggregate"))
            {
                return;
            }

            var collection = e.Command.GetValue(e.CommandName, BsonNull.Value);
            if (collection.IsString && collection.AsString is "EmailTemplates" or "MailServerConfigurations")
            {
                Interlocked.Increment(ref _selectionReads);
            }
        }

        private IMongoCollection<BsonDocument> Templates() => _database.GetCollection<BsonDocument>("EmailTemplates");
        private IMongoCollection<BsonDocument> Configurations() => _database.GetCollection<BsonDocument>("MailServerConfigurations");
        private IMongoCollection<BsonDocument> Users() => _database.GetCollection<BsonDocument>("Users");

        /// <summary>A template document; <paramref name="organizationId"/> null leaves the element out.</summary>
        private static BsonDocument Template(string id, string? organizationId, string? configurationId, string name = Purpose, string language = Language)
        {
            var doc = new BsonDocument
            {
                { "_id", id }, { "Name", name }, { "Language", language }, { "MailConfigurationId", configurationId ?? "" },
                { "TemplateBody", $"<p>{id}</p>" }, { "TemplateSubject", $"Subject {id}" }
            };
            if (organizationId is not null) doc.Add("OrganizationId", organizationId);
            return doc;
        }

        /// <summary>A configuration document; optional elements are left out when null, like documents written before them.</summary>
        private static BsonDocument Configuration(string id, string? organizationId, bool? isDefault = null, bool? isOrganizationDefault = null, bool? isInbound = null)
        {
            var doc = new BsonDocument { { "_id", id }, { "Name", id }, { "Host", "smtp.test" }, { "Port", 587 }, { "SenderAddress", "noreply@app.test" } };
            if (organizationId is not null) doc.Add("OrganizationId", organizationId);
            if (isDefault is not null) doc.Add("IsDefault", isDefault.Value);
            if (isOrganizationDefault is not null) doc.Add("IsOrganizationDefault", isOrganizationDefault.Value);
            if (isInbound is not null) doc.Add("IsInbound", isInbound.Value);
            return doc;
        }

        // §7: T1 OrderConfirmation/en-US/default (C-def), T2 the same in acme (no configuration);
        // C-def project default, C-acme acme's organization default, C-in acme inbound.
        private void SeedSection7()
        {
            Templates().InsertMany([Template("T1", "default", "C-def"), Template("T2", "acme", "")]);
            Configurations().InsertMany([
                Configuration("C-def", "default", isDefault: true),
                Configuration("C-acme", "acme", isOrganizationDefault: true),
                Configuration("C-in", "acme", isInbound: true)
            ]);
        }

        private static void SignIn(string? organizationId) =>
            BlocksContext.SetContext(BlocksContext.Create("t1", [], "caller", true, "/api/mail", organizationId, DateTime.UtcNow.AddMinutes(5),
                Recipient, [], "caller", "", "Caller", "token", "t1"));

        private static SendMail Send(string purpose = Purpose, string language = Language) =>
            new() { Purpose = purpose, Language = language, To = [Recipient] };

        private async Task<BaseMutationResponse> HttpSend(string? tokenOrganization, string purpose = Purpose, string language = Language)
        {
            SignIn(tokenOrganization);
            _selectionReads = 0;
            _sent = null;
            return await _service.ProcessMailAsync(Send(purpose, language));
        }

        private void ExpectSent(string templateId, string configurationId)
        {
            _sent.Should().NotBeNull();
            _sent!.EmailTemplate.ItemId.Should().Be(templateId);
            _sent.MailServerConfiguration.ItemId.Should().Be(configurationId);
            _selectionReads.Should().BeLessThanOrEqualTo(2, "H10: one read chooses the template, one the configuration");
        }

        [Fact]
        public async Task Example1_H1_H5_TokenOrganizationTemplate_UsesTheOrganizationDefaultConfiguration()
        {
            var response = await HttpSend("acme");

            response.IsSuccess.Should().BeTrue();
            ExpectSent("T2", "C-acme");
            _selectionReads.Should().Be(2);
        }

        [Fact]
        public async Task Example2_H2_H4_OrganizationWithoutTemplate_UsesTheDefaultTemplateAndItsConfiguration()
        {
            (await HttpSend("globex")).IsSuccess.Should().BeTrue();

            ExpectSent("T1", "C-def");
        }

        [Fact]
        public async Task Example3_H3_QueueSend_UsesTheDefaultOrganization_WhateverTheContextSays()
        {
            SignIn("acme");
            _selectionReads = 0;

            (await _service.ProcessQueuedMailAsync(Send())).IsSuccess.Should().BeTrue();

            ExpectSent("T1", "C-def");
        }

        [Fact]
        public async Task Example4_C1_InboundTemplateConfiguration_IsSkippedForTheOrganizationDefault()
        {
            Templates().UpdateOne(Builders<BsonDocument>.Filter.Eq("_id", "T2"), Builders<BsonDocument>.Update.Set("MailConfigurationId", "C-in"));

            (await HttpSend("acme")).IsSuccess.Should().BeTrue();

            ExpectSent("T2", "C-acme");
        }

        [Fact]
        public async Task Example5_H7_TestMailByTemplateId_SendsThatTemplate_WithItsOrganizationForTheConfiguration()
        {
            SignIn("default");
            _selectionReads = 0;

            var response = await _service.ProcessMailToAnyAsync(new SendMailToAny
            {
                IsTestMail = true, TemplateId = "T2", Purpose = "SomethingElse", Language = "zz-ZZ", To = [Recipient]
            });

            response.IsSuccess.Should().BeTrue();
            ExpectSent("T2", "C-acme");
            _sent!.Name.Should().Be(Purpose, "6.5: Purpose and Language come from the template");
            _sent.Language.Should().Be(Language);
        }

        [Fact]
        public async Task Example6_C5_NoTemplateForTheLanguageInAnyOrganization_Returns400WithTheTemplateMessage()
        {
            var response = await HttpSend("acme", language: "zz-ZZ");

            response.IsSuccess.Should().BeFalse();
            response.Errors.Should().ContainKey("Name").WhoseValue.Should().Be(TemplateMissingMessage);
            _sender.Verify(s => s.ProcessSendMailAsync(It.IsAny<MailToBeSent>()), Times.Never);
            _selectionReads.Should().BeLessThanOrEqualTo(2);
        }

        [Fact]
        public async Task Example7_H9_TemplateWithoutOrganizationId_IsADefaultTemplate()
        {
            Templates().InsertOne(Template("T3", organizationId: null, configurationId: "C-def", name: "Welcome"));

            (await HttpSend("acme", purpose: "Welcome")).IsSuccess.Should().BeTrue();

            ExpectSent("T3", "C-def");
        }

        [Theory]
        [InlineData("")]
        [InlineData(null)]
        public async Task EmptyTemplateOrganizationId_IsADefaultTemplate(string? stored)
        {
            var doc = Template("T3", organizationId: null, configurationId: "C-def", name: "Welcome");
            doc.Add("OrganizationId", stored is null ? BsonNull.Value : stored);
            Templates().InsertOne(doc);

            (await HttpSend("acme", purpose: "Welcome")).IsSuccess.Should().BeTrue();

            ExpectSent("T3", "C-def");
        }

        [Fact]
        public async Task H5_DefaultOrganization_UsesItsOrganizationDefaultConfiguration_StoredWithoutOrganizationId()
        {
            Templates().InsertOne(Template("T4", "default", "", name: "Invoice"));
            Configurations().InsertOne(Configuration("C-org-def", organizationId: null, isOrganizationDefault: true));

            (await HttpSend("default", purpose: "Invoice")).IsSuccess.Should().BeTrue();

            ExpectSent("T4", "C-org-def");
        }

        [Fact]
        public async Task H6_NoTemplateConfigurationAndNoOrganizationDefault_UsesTheProjectDefault()
        {
            Templates().InsertOne(Template("T4", "default", "", name: "Invoice"));

            (await HttpSend("globex", purpose: "Invoice")).IsSuccess.Should().BeTrue();

            ExpectSent("T4", "C-def");
        }

        [Fact]
        public async Task H6_AnotherOrganizationsDefault_IsNeverUsed()
        {
            Templates().InsertOne(Template("T4", "default", "", name: "Invoice"));

            (await HttpSend("default", purpose: "Invoice")).IsSuccess.Should().BeTrue();

            ExpectSent("T4", "C-def");
        }

        [Fact]
        public async Task C2_InboundOrganizationAndProjectDefaults_AreNotUsed_C4_Returns400WithTheConfigurationMessage()
        {
            Configurations().DeleteMany(FilterDefinition<BsonDocument>.Empty);
            Configurations().InsertMany([
                Configuration("C-def-in", "default", isDefault: true, isInbound: true),
                Configuration("C-acme-in", "acme", isOrganizationDefault: true, isInbound: true)
            ]);

            var response = await HttpSend("acme");

            response.IsSuccess.Should().BeFalse();
            response.Errors.Should().ContainKey("Name").WhoseValue.Should().Be(ConfigurationMissingMessage);
            _sender.Verify(s => s.ProcessSendMailAsync(It.IsAny<MailToBeSent>()), Times.Never);
            _selectionReads.Should().Be(2);
        }

        [Fact]
        public async Task C3_DeletedTemplateConfiguration_FallsBackToTheOrganizationDefault_ThenTheProjectDefault()
        {
            Templates().UpdateOne(Builders<BsonDocument>.Filter.Eq("_id", "T2"), Builders<BsonDocument>.Update.Set("MailConfigurationId", "C-gone"));

            (await HttpSend("acme")).IsSuccess.Should().BeTrue();
            ExpectSent("T2", "C-acme");

            Configurations().DeleteOne(Builders<BsonDocument>.Filter.Eq("_id", "C-acme"));
            (await HttpSend("acme")).IsSuccess.Should().BeTrue();
            ExpectSent("T2", "C-def");
        }

        [Fact]
        public async Task C5_OnlyAnotherOrganizationHasTheTemplate_Returns400()
        {
            Templates().InsertOne(Template("T5", "other", "C-def", name: "Private"));

            var response = await HttpSend("acme", purpose: "Private");

            response.IsSuccess.Should().BeFalse();
            response.Errors.Should().ContainKey("Name").WhoseValue.Should().Be(TemplateMissingMessage);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("  ")]
        public async Task C6_TokenWithoutOrganization_UsesTheDefaultOrganization(string? tokenOrganization)
        {
            (await HttpSend(tokenOrganization)).IsSuccess.Should().BeTrue();

            ExpectSent("T1", "C-def");
        }

        [Fact]
        public async Task C7_OrganizationFieldsInTheBody_AreIgnored()
        {
            var request = System.Text.Json.JsonSerializer.Deserialize<SendMailToAny>(
                $$"""{ "Purpose": "{{Purpose}}", "Language": "{{Language}}", "To": ["{{Recipient}}"], "OrganizationId": "acme", "organizationIds": ["acme"] }""")!;
            SignIn("globex");
            _selectionReads = 0;

            (await _service.ProcessMailToAnyAsync(request)).IsSuccess.Should().BeTrue();

            ExpectSent("T1", "C-def");
        }

        [Fact]
        public async Task C8_TemplateIdWithoutTestMail_IsIgnored()
        {
            SignIn("acme");
            _selectionReads = 0;

            (await _service.ProcessMailToAnyAsync(new SendMailToAny { TemplateId = "T1", Purpose = Purpose, Language = Language, To = [Recipient] }))
                .IsSuccess.Should().BeTrue();

            ExpectSent("T2", "C-acme");
        }

        [Fact]
        public async Task C9_UnknownTemplateId_Returns400WithTheTemplateMessage()
        {
            SignIn("default");
            _selectionReads = 0;

            var response = await _service.ProcessMailToAnyAsync(new SendMailToAny
            {
                IsTestMail = true, TemplateId = "no-such-template", Purpose = Purpose, Language = Language, To = [Recipient]
            });

            response.IsSuccess.Should().BeFalse();
            response.Errors.Should().ContainKey("Name").WhoseValue.Should().Be(TemplateMissingMessage);
            _selectionReads.Should().BeLessThanOrEqualTo(2);
        }

        [Fact]
        public async Task C9_UnknownTemplateId_WithoutPurpose_StillReportsTheTemplateMessage()
        {
            SignIn("default");

            var response = await _service.ProcessMailToAnyAsync(new SendMailToAny
            {
                IsTestMail = true, TemplateId = "no-such-template", Language = Language, To = [Recipient]
            });

            response.Errors.Should().ContainKey("Name").WhoseValue.Should().Be(TemplateMissingMessage);
        }

        [Fact]
        public async Task C10_DuplicateTemplatesInTheOrganization_PickTheLowestItemId_AndWarnWithBothIds()
        {
            Templates().InsertOne(Template("T0-acme-dup", "acme", ""));

            (await HttpSend("acme")).IsSuccess.Should().BeTrue();

            ExpectSent("T0-acme-dup", "C-acme");
            _logger.Lines.Should().Contain(l => l.Level == LogLevel.Warning && l.Message.Contains("T0-acme-dup") && l.Message.Contains("T2"));
        }

        [Fact]
        public async Task C10_ManyLowerDefaultDuplicates_NeverHideTheOrganizationsOwnTemplate()
        {
            Templates().InsertMany([Template("A1", "default", "C-def"), Template("A2", "default", "C-def"), Template("A3", null, "C-def")]);

            (await HttpSend("acme")).IsSuccess.Should().BeTrue();

            ExpectSent("T2", "C-acme");
            _logger.Lines.Should().NotContain(l => l.Level == LogLevel.Warning && l.Message.Contains("T2"));
        }

        [Fact]
        public async Task C10_DuplicatesInTheDefaultOrganization_IncludingMissingOrganizationId_PickTheLowestItemId()
        {
            Templates().InsertOne(Template("T0-default-dup", organizationId: null, configurationId: "C-def"));

            (await HttpSend("globex")).IsSuccess.Should().BeTrue();

            ExpectSent("T0-default-dup", "C-def");
            _logger.Lines.Should().Contain(l => l.Level == LogLevel.Warning && l.Message.Contains("T0-default-dup") && l.Message.Contains("T1"));
        }

        [Fact]
        public async Task H8_SingleOrganizationProject_ChoosesTheSameTemplateAndConfigurationAsBefore()
        {
            // Everything in the Default organization: the template and the configuration it names, as before.
            Templates().DeleteOne(Builders<BsonDocument>.Filter.Eq("_id", "T2"));
            Configurations().DeleteMany(Builders<BsonDocument>.Filter.Eq("OrganizationId", "acme"));
            Configurations().InsertOne(Configuration("C-other", organizationId: null));
            Templates().InsertOne(Template("T6", organizationId: null, configurationId: "C-other", name: "Reset"));

            foreach (var token in new[] { "default", "", "acme" })
            {
                (await HttpSend(token)).IsSuccess.Should().BeTrue();
                ExpectSent("T1", "C-def");
                (await HttpSend(token, purpose: "Reset")).IsSuccess.Should().BeTrue();
                ExpectSent("T6", "C-other");
            }
        }

        [Fact]
        public async Task H11_MappingLogsTheOrganizationAndTheConfigurationStep()
        {
            await HttpSend("acme");

            _logger.Lines.Should().Contain(l => l.Message.StartsWith("MAIL MAPPING")
                && l.Message.Contains("organizationId=acme")
                && l.Message.Contains("templateOrganizationId=acme")
                && l.Message.Contains("configurationSource=organization-default"));
            _logger.Lines.Should().NotContain(l => l.Message.StartsWith("MAIL MAPPING") && l.Message.Contains(Recipient));
        }

        [Fact]
        public async Task H11_MissingConfigurationIsLoggedWithSourceNone()
        {
            Configurations().DeleteMany(FilterDefinition<BsonDocument>.Empty);

            await HttpSend("acme");

            _logger.Lines.Should().Contain(l => l.Level == LogLevel.Error && l.Message.StartsWith("MAIL MAPPING")
                && l.Message.Contains("organizationId=acme") && l.Message.Contains("configurationSource=none"));
        }

        internal sealed record LogLine(LogLevel Level, string Message);

        /// <summary>Keeps each formatted line with its level.</summary>
        internal sealed class RecordingLogger : ILogger<MailService>
        {
            private readonly List<LogLine> _lines = [];

            public IReadOnlyList<LogLine> Lines
            {
                get { lock (_lines) return _lines.ToList(); }
            }

            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                lock (_lines) _lines.Add(new LogLine(logLevel, formatter(state, exception)));
            }
        }
    }
}
