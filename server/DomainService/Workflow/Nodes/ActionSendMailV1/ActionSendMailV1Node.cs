using Mail.DomainService;
using System.Text.Json;
using Blocks.Genesis;
using MongoDB.Bson;
using Mail.DomainService.Mails;

namespace DomainService.Workflow.Nodes.ActionSendMailV1
{
    /// <summary>
    /// Action node that sends email using mail driver service
    /// Takes email configuration and processes each input item
    /// </summary>
    public class ActionSendMailV1Node : NodeExecutorBase<ActionSendMailV1Parameters>
    {
        public override string NodeType => "sendMail";
        public override string Version => "1.0";

        private readonly IMailService _mailDriverService;

        public ActionSendMailV1Node( IMailService mailDriverService )
        {
            _mailDriverService = mailDriverService;
        }

        protected override async Task<NodeExecutionResult> ExecuteAsync(NodeExecutionContext context, ActionSendMailV1Parameters? nodeparameters)
        {
            try
            {
                var parameters = nodeparameters ?? new ActionSendMailV1Parameters();
                var outputItems = new List<NodeOutputItem>();
                var blocksContext = BlocksContext.GetContext();
                for (int i = 0; i < context.IterationCount; i++)
                {
                    var to = parseExpression<string>(parameters.To, context.InputItems[i], context) ?? "";
                    var bodyDataContext = parameters.BodyDataContext.Keys.ToDictionary(key => key, key => parseExpression<string>(parameters.BodyDataContext[key], context.InputItems[i], context) ?? "");

                    // Each entry is either a literal storage File ID, or an expression (same
                    // {{$json...}}/{{$node[...]...}}/{{$context...}} syntax as `To`) resolved
                    // per-iteration. Mail.DomainService's StorageMailAttachmentResolver already
                    // drops blanks and de-duplicates, so no extra handling is needed here.
                    var attachments = (parameters.Attachments ?? new List<string>())
                        .Select(a => parseExpression<string>(a, context.InputItems[i], context) ?? a)
                        .ToList();

                    var projectkey = parameters.ProjectKey ?? "";
                    var securityData = BlocksContext.Create(projectkey, [], "", false, "", "", DateTime.MinValue, "", [], "", "", "", "", "", projectkey);
                    BlocksContext.SetContext(securityData, false);
                    var response = await SendMailAsync(projectkey, to, parameters.Template, parameters.Language, bodyDataContext, attachments);
                    BlocksContext.SetContext(blocksContext, false);
                    // Built as a plain BsonDocument (not a Dictionary<string, object> run through
                    // ToBsonDocument()) so non-primitive values (the Errors document, the
                    // AttachmentsSent array) serialize as themselves rather than getting wrapped
                    // in a polymorphic discriminator, which is what happens when the driver's
                    // object serializer sees a declared type of `object` for a BsonValue/array.
                    var output = new BsonDocument
                    {
                        { "Success", response.IsSuccess },
                        { "Errors", response.Errors != null ? response.Errors.ToBsonDocument() : BsonNull.Value },
                        { "To", to },
                        { "AttachmentsSent", new BsonArray(attachments) },
                        { "AttachmentCount", attachments.Count }
                    };

                    outputItems.Add(new NodeOutputItem
                    {
                        Data = new NodeOutputItemData
                        {
                            Input = context.InputItems[i].Data.Output,
                            Output = output,
                            Parameters = parameters.ToBsonDocument(),
                        },
                        Branch = "source",
                        ParentItemIds = new List<string>() { context.InputItems[i].Id },
                    });
                }

                return NodeExecutionResult.Successful(outputItems);
            }
            catch (Exception ex)
            {

                return NodeExecutionResult.Failed(ex.Message);
            }
        }

        public static Task<bool> ValidateConfigurationAsync(JsonDocument parameters)
        {
            try
            {
                var config = JsonSerializer.Deserialize<ActionSendMailV1Parameters>(parameters);
                var isValid = config != null &&
                              !string.IsNullOrEmpty(config.To) &&
                              !string.IsNullOrEmpty(config.Template);
                return Task.FromResult(isValid);
            }
            catch
            {
                return Task.FromResult(false);
            }
        }


        private async Task<BaseMutationResponse> SendMailAsync(string projectkey, string to, string template, string language, Dictionary<string, string> bodyDataContext, List<string> attachments)
        {
            var email = new SendMailToAny
            {
                Cc = Array.Empty<string>(),
                Bcc = Array.Empty<string>(),
                BodyDataContext = bodyDataContext,
                Purpose = template,
                Language = language ?? "en-US",
                To = new List<string>() { to.Trim() },
                Attachments = attachments,
                ProjectKey = projectkey
            };

            var response = await _mailDriverService.ProcessMailToAnyAsync(email);
            return response;
        }

    }
}
