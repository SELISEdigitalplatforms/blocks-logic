using System.Text.Json;
using FluentAssertions;
using Functions.DomainService.Dtos.Requests;
using Functions.DomainService.Dtos.Responses;
using Functions.DomainService.Entities;
using Functions.DomainService.Models;
using Functions.DomainService.Validation;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;

namespace XUnitTest.Functions
{
    /// <summary>
    /// The rate-limit fields a Save may carry (FN-19). Before, <see cref="FunctionLimits.Clamp"/>
    /// silently dropped a per-day value and an out-of-range per-minute one, so the caller asked
    /// for a limit and got none without being told. Now the request is refused with 400; null still
    /// means "the default", and old stored documents keep loading and running.
    /// </summary>
    public class FunctionLimitsValidatorTests
    {
        private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

        private static SaveFunctionRequestDto Save(int? perMinute = null, int? perDay = null) => new()
        {
            FunctionId = "fn-1",
            IndexJs = "export default async function (input) { return input; }",
            PackageJson = """{"type":"module"}""",
            Limits = new FunctionLimits { RequestsPerMinute = perMinute, RequestsPerDay = perDay },
        };

        private static List<string> Errors(SaveFunctionRequestDto request) =>
            new SaveFunctionRequestValidator().Validate(request).Errors.Select(e => e.ErrorMessage).ToList();

        [Theory]
        [InlineData(1)]
        [InlineData(100_000)]
        [InlineData(600)]
        public void Per_minute_inside_the_range_is_accepted(int perMinute)
        {
            Errors(Save(perMinute: perMinute)).Should().BeEmpty();
        }

        [Fact]
        public void Per_minute_null_is_the_default_and_accepted()
        {
            Errors(Save(perMinute: null)).Should().BeEmpty();
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        [InlineData(100_001)]
        [InlineData(int.MinValue)]
        [InlineData(int.MaxValue)]
        public void Per_minute_outside_the_range_is_refused_with_the_range(int perMinute)
        {
            Errors(Save(perMinute: perMinute)).Should().ContainSingle()
                .Which.Should().Be("requestsPerMinute must be between 1 and 100000");
        }

        [Theory]
        [InlineData(1)]
        [InlineData(0)]
        [InlineData(-5)]
        [InlineData(5000)]
        public void Any_per_day_value_is_refused(int perDay)
        {
            Errors(Save(perDay: perDay)).Should().ContainSingle()
                .Which.Should().Be("requestsPerDay is not supported; use requestsPerMinute");
        }

        [Fact]
        public void Per_day_and_a_bad_per_minute_both_report()
        {
            Errors(Save(perMinute: 0, perDay: 10)).Should().BeEquivalentTo(
                "requestsPerDay is not supported; use requestsPerMinute",
                "requestsPerMinute must be between 1 and 100000");
        }

        [Fact]
        public void The_editors_own_payload_is_still_accepted()
        {
            // What the console sends today: fixed cpu/memory/timeout/concurrency (ignored) and nulls.
            const string json = """
            {"functionId":"fn-1","indexJs":"x","packageJson":"{\"type\":\"module\"}",
             "limits":{"cpuMillicores":100,"memoryMb":192,"timeoutSeconds":10,"concurrency":2,
                       "requestsPerMinute":null,"requestsPerDay":null}}
            """;
            var request = JsonSerializer.Deserialize<SaveFunctionRequestDto>(json, Web)!;

            Errors(request).Should().BeEmpty();
        }

        [Fact]
        public void Cpu_memory_timeout_and_concurrency_are_still_not_checked()
        {
            var request = Save();
            request.Limits.CpuMillicores = -1;
            request.Limits.MemoryMb = 999_999;
            request.Limits.TimeoutSeconds = 0;
            request.Limits.Concurrency = -3;

            Errors(request).Should().BeEmpty("they are fixed and Clamp discards them");
        }

        [Fact]
        public void An_old_stored_document_with_per_day_still_loads_and_runs_on_the_profile()
        {
            var stored = new BsonDocument
            {
                { "CpuMillicores", 100 }, { "MemoryMb", 192 }, { "TimeoutSeconds", 10 }, { "Concurrency", 2 },
                { "RequestsPerMinute", 50 }, { "RequestsPerDay", 5000 },
            };

            var limits = BsonSerializer.Deserialize<FunctionLimits>(stored);
            limits.RequestsPerDay.Should().Be(5000, "the old value must still deserialize");

            var clamped = limits.Clamp();
            clamped.RequestsPerMinute.Should().Be(50);
            clamped.RequestsPerDay.Should().BeNull();
            clamped.MemoryMb.Should().Be(FunctionLimits.Ceiling.MemoryMb);
        }

        [Fact]
        public void The_editor_gets_clamped_limits_so_saving_an_old_function_back_is_not_refused()
        {
            // The editor sends back what the detail gave it. An old document's per-day value must not
            // reach it, or the first Save after this change would answer 400.
            var function = new FunctionEntity
            {
                ItemId = "fn-1",
                Limits = new FunctionLimits { RequestsPerMinute = 50, RequestsPerDay = 5000 },
            };

            var detail = FunctionDetailDto.From(function);

            detail.Limits.RequestsPerDay.Should().BeNull();
            detail.Limits.RequestsPerMinute.Should().Be(50);
            var request = Save();
            request.Limits = detail.Limits;
            Errors(request).Should().BeEmpty();
        }

        [Fact]
        public void A_missing_stored_limits_object_still_gives_the_profile()
        {
            var function = new FunctionEntity { ItemId = "fn-1", Limits = null! };

            FunctionDetailDto.From(function).Limits.CpuMillicores.Should().Be(FunctionLimits.Ceiling.CpuMillicores);
        }
    }
}
