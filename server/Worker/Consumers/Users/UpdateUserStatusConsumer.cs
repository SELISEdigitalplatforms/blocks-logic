using Blocks.Genesis;
using Iam.DomainService.Shared.Dtos;


namespace Worker.Consumers.Users
{
    public class UserStatusChangedConsumer : IConsumer<UserStatusChangedEvent>
    {
        private readonly IHttpService _httpService;
        private const string _verioSyntemApiKey = "f6a2c2e1-7bb5-4967-96bf-c534bb1f6c14";
        private const string _verioSystemBaseUri = "https://variosystems.seliselocal.com/api/business-variosystems/ActivateDeactivateUser";

        public UserStatusChangedConsumer(IHttpService httpService)
        {
            _httpService = httpService;
        }

        public async Task  Consume(UserStatusChangedEvent context)
        {
             context.ApiKey = _verioSyntemApiKey;
             await _httpService.Put<UserStatusChangedEvent>(context, _verioSystemBaseUri);
        }
    }
}
