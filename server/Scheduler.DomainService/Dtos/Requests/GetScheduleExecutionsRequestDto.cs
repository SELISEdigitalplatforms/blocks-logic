using System.ComponentModel.DataAnnotations;

namespace Scheduler.DomainService.Dtos.Requests
{
    /// <summary>
    /// One page of a schedule's execution history, newest first. Modelled on <c>WorkflowGetsRequestDto</c>:
    /// invalid input is rejected by <c>[ApiController]</c> model validation (HTTP 400) before any query runs.
    /// </summary>
    public class GetScheduleExecutionsRequestDto : IValidatableObject
    {
        [Required]
        [StringLength(64)]
        public string ScheduleId { get; set; } = string.Empty;

        /// <summary><c>Succeeded</c>, <c>Failed</c>, or null for all.</summary>
        [RegularExpression("^(Succeeded|Failed)$", ErrorMessage = "Outcome must be Succeeded or Failed")]
        public string? Outcome { get; set; }

        [Range(1, 50, ErrorMessage = "PageSize must be between 1 and 50")]
        public int PageSize { get; set; } = 10;

        [Range(0, int.MaxValue, ErrorMessage = "PageNumber must be greater than or equal to 0")]
        public int PageNumber { get; set; } = 0;

        /// <summary>Inclusive lower bound on <c>FiredUtc</c> (UTC). Null = no lower bound.</summary>
        public DateTime? StartDate { get; set; }

        /// <summary>Exclusive upper bound on <c>FiredUtc</c> (UTC). Null = no upper bound.</summary>
        public DateTime? EndDate { get; set; }

        public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
        {
            if (StartDate is { } start && EndDate is { } end && ToUtc(end) <= ToUtc(start))
            {
                yield return new ValidationResult("EndDate must be after StartDate", [nameof(EndDate)]);
            }
        }

        /// <summary>
        /// The bound as UTC: an <c>Unspecified</c> value is taken as UTC, a <c>Local</c> one converted.
        /// </summary>
        public static DateTime ToUtc(DateTime value) => value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
        };
    }
}
