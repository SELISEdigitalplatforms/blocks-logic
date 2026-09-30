namespace Workflow.DomainService.Dtos
{
    public class WorkflowExecutionsGetRequestDto
    {
        public required string WorkflowId { get; set; }

        /// <summary>
        /// When null, the endpoint returns the full list (the historical contract).
        /// When set, must be 1..100 and selects a cursor page.
        /// </summary>
        public int? PageSize { get; set; }

        /// <summary>Return the next older page after this execution. Mutually exclusive with <see cref="AfterId"/>.</summary>
        public string? BeforeId { get; set; }

        /// <summary>
        /// Return runs strictly newer than this execution, oldest of those first.
        /// Mutually exclusive with <see cref="BeforeId"/>.
        /// </summary>
        public string? AfterId { get; set; }

        /// <summary>
        /// Status refresh for rows the client already has. Only applied with <see cref="AfterId"/>.
        /// Extra ids past 50 are ignored.
        /// </summary>
        public List<string>? RefreshIds { get; set; }
    }
}
