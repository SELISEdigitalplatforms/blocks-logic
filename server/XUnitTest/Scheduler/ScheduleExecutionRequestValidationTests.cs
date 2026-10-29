using System.ComponentModel.DataAnnotations;
using System.Reflection;
using BlocksTemplate.Api.Controllers;
using Blocks.Genesis;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Scheduler.DomainService.Dtos.Requests;

namespace XUnitTest.Scheduler
{
    /// <summary>
    /// The DataAnnotations <c>[ApiController]</c> enforces (HTTP 400 before the action runs) on the two history
    /// requests (C13), and the read permission on both actions (C9).
    /// </summary>
    public class ScheduleExecutionRequestValidationTests
    {
        private static List<ValidationResult> Validate(object dto)
        {
            var results = new List<ValidationResult>();
            Validator.TryValidateObject(dto, new ValidationContext(dto), results, validateAllProperties: true);
            return results;
        }

        private static GetScheduleExecutionsRequestDto Valid() => new() { ScheduleId = "s1" };

        [Fact]
        public void List_Defaults_AreValid()
        {
            var dto = Valid();

            dto.PageSize.Should().Be(10);
            dto.PageNumber.Should().Be(0);
            Validate(dto).Should().BeEmpty();
        }

        public static TheoryData<Action<GetScheduleExecutionsRequestDto>, string> InvalidLists => new()
        {
            { d => d.PageSize = 500, "PageSize must be between 1 and 50" },
            { d => d.PageSize = 0, "PageSize must be between 1 and 50" },
            { d => d.PageNumber = -1, "PageNumber must be greater than or equal to 0" },
            { d => d.Outcome = "Skipped", "Outcome must be Succeeded or Failed" },
            { d => d.Outcome = "succeeded", "Outcome must be Succeeded or Failed" },
            { d => d.ScheduleId = null!, "The ScheduleId field is required." },
            { d => d.ScheduleId = new string('x', 65), "The field ScheduleId must be a string with a maximum length of 64." },
            {
                d => { d.StartDate = new DateTime(2026, 10, 27, 0, 0, 0, DateTimeKind.Utc); d.EndDate = new DateTime(2026, 10, 26, 0, 0, 0, DateTimeKind.Utc); },
                "EndDate must be after StartDate"
            },
            {
                d => { d.StartDate = new DateTime(2026, 10, 26, 0, 0, 0, DateTimeKind.Utc); d.EndDate = d.StartDate; },
                "EndDate must be after StartDate"
            },
        };

        [Theory]
        [MemberData(nameof(InvalidLists))]
        public void List_Invalid_IsRejected(Action<GetScheduleExecutionsRequestDto> change, string message)
        {
            var dto = Valid();
            change(dto);

            Validate(dto).Select(r => r.ErrorMessage).Should().Contain(message);
        }

        [Fact]
        public void List_EndDateRule_IsReportedOnEndDate()
        {
            var dto = Valid();
            dto.StartDate = new DateTime(2026, 10, 27, 0, 0, 0, DateTimeKind.Utc);
            dto.EndDate = new DateTime(2026, 10, 26, 0, 0, 0, DateTimeKind.Utc);

            Validate(dto).Single().MemberNames.Should().Equal("EndDate");
        }

        [Theory]
        [InlineData("Succeeded")]
        [InlineData("Failed")]
        [InlineData(null)]
        public void List_KnownOutcomes_AreValid(string? outcome)
        {
            var dto = Valid();
            dto.Outcome = outcome;
            dto.PageSize = 50;
            dto.StartDate = new DateTime(2026, 10, 26, 0, 0, 0, DateTimeKind.Utc);
            dto.EndDate = new DateTime(2026, 10, 27, 0, 0, 0, DateTimeKind.Utc);

            Validate(dto).Should().BeEmpty();
        }

        [Fact]
        public void Detail_RequiresBothIds_AtMost64Chars()
        {
            Validate(new GetScheduleExecutionRequestDto { ScheduleId = "s1", ExecutionId = "e1" }).Should().BeEmpty();
            Validate(new GetScheduleExecutionRequestDto { ScheduleId = "s1", ExecutionId = null! }).Should().ContainSingle();
            Validate(new GetScheduleExecutionRequestDto { ScheduleId = new string('x', 65), ExecutionId = "e1" }).Should().ContainSingle();
        }

        [Theory]
        [InlineData(nameof(SchedulerController.GetScheduleExecutions))]
        [InlineData(nameof(SchedulerController.GetScheduleExecution))]
        [InlineData(nameof(SchedulerController.PreviewScheduleRuns))]
        public void Actions_ArePostAndNeedSchedulerRead(string action)
        {
            var method = typeof(SchedulerController).GetMethod(action)!;

            method.GetCustomAttribute<HttpPostAttribute>().Should().NotBeNull();
            method.GetCustomAttribute<ProtectedEndPointAttribute>()!.ResourceName.Should().Be("blocks-logic::scheduler::read");
            typeof(SchedulerController).GetCustomAttribute<ApiControllerAttribute>().Should().NotBeNull();
        }
    }
}
