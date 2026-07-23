using Blocks.Genesis;

namespace DomainService.Projects
{
    public class UpdateProjectRequest 
    {
        public string? CustomDomain { get; set; }
        public string ApplicationDomain { get; set; }
    }
}
