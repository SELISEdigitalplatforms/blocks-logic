using Blocks.MailDriver;
using System.Text.Json;
using Blocks.Genesis;
using MongoDB.Bson;
using System.Diagnostics.CodeAnalysis;

namespace DomainService.Workflow.Nodes.ActionSendMailV1
{
    /// <summary>
    /// Action node that sends email using mail driver service
    /// Takes email configuration and processes each input item
    /// </summary>
    [ExcludeFromCodeCoverage]
    public class ActionSendMailV1Node : NodeExecutorBase<ActionSendMailV1Parameters>
    {
        public override string NodeType => "sendMail";
        public override string Version => "1.0";

        private readonly IMailDriverService _mailDriverService;

        public ActionSendMailV1Node(IMailDriverService mailDriverService)
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

                    var projectkey = parameters.ProjectKey ?? "";
                    var securityData = BlocksContext.Create(projectkey, [], "", false, "", "", DateTime.MinValue, "", [], "", "", "", "", "", projectkey);
                    BlocksContext.SetContext(securityData, false);
                    var response = await SendMailAsync(projectkey, to, parameters.Template, parameters.Language, bodyDataContext);
                    BlocksContext.SetContext(blocksContext, false);
                    var output = new Dictionary<string, object>
                    {
                        { "Success", response.IsSuccess },
                        { "Errors", response.Errors?.ToBsonDocument() ?? null },
                        { "To", to }
                    };

                    outputItems.Add(new NodeOutputItem
                    {
                        Data = new NodeOutputItemData
                        {
                            Input = context.InputItems[i].Data.Output,
                            Output = output.ToBsonDocument(),
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


        private async Task<BaseMutationResponse> SendMailAsync(string projectkey, string to, string template, string language, Dictionary<string, string> bodyDataContext)
        {
            var email = new SendMailToAny
            {
                Cc = Array.Empty<string>(),
                Bcc = Array.Empty<string>(),
                BodyDataContext = bodyDataContext,
                Purpose = template,
                Language = language ?? "en-US",
                To = new List<string>() { to.Trim() },
                ProjectKey = projectkey
            };

            var response = await _mailDriverService.SendToAnyAsync(email);
            return response;
        }

    }
}
