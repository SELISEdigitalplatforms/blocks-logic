using DomainService.Entities;

namespace DomainService.Dtos
{
    public class GroupedProjectsDto
    {
        public string TenantGroupId { get; set; } = string.Empty;
        public List<Project> Projects { get; set; } = [];
        public bool IsShared { get; set; }

        /// <summary>
        /// The caller's grants in this group, unioned across their rows. Empty for a group they
        /// own — an owner holds everything implicitly.
        /// </summary>
        public List<string> AccessPolicies { get; set; } = [];
        public List<Project> NonSharedProject { get; set; } = [];
    }
}
