using Blocks.Genesis;

namespace Iam.DomainService.Users
{
    public class IsEmailAvaiableRequest
    {
        public string Email { get; set; }
    }

    public class IsEmailAvaiableResponse
    {
        public bool IsAvailable { get; set; }
    }
}
