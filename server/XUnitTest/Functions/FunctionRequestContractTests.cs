using System.Text.Json;
using BlocksTemplate.Api;
using FluentAssertions;
using Functions.DomainService.Dtos.Requests;
using Functions.DomainService.Enums;
using Functions.DomainService.Utils;
using Functions.DomainService.Validation;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;

namespace XUnitTest.Functions
{
    /// <summary>
    /// What the Studio sends and what the API is willing to accept, in both directions. Each case
    /// here stands for a report from the product: a save that could not be persisted, a field the
    /// dialog refused but the API took, a failure that reached the client as a 500.
    /// </summary>
    public class FunctionRequestContractTests
    {
        /// <summary>The options MVC itself binds JSON bodies with.</summary>
        private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

        // ---- the wire contract -------------------------------------------------------

        [Theory]
        [InlineData("Post", HttpTriggerMethod.Post)]
        [InlineData("Get", HttpTriggerMethod.Get)]
        public void A_trigger_method_arrives_as_the_name_the_client_sends(string wire, HttpTriggerMethod expected)
        {
            // The client's ITriggerConfig declares this as a string union and sends "Post"/"Get".
            // Without a string converter on the enum, Save answered 400 with "The JSON value could
            // not be converted to … HttpTriggerMethod. Path: $.trigger.httpMethod" — which meant no
            // trigger setting on an existing function could ever be changed.
            var json = """{"functionId":"fn-1","trigger":{"httpMethod":"WIRE"}}""".Replace("WIRE", wire, StringComparison.Ordinal);

            var request = JsonSerializer.Deserialize<SaveFunctionRequestDto>(json, Web);

            request!.Trigger.HttpMethod.Should().Be(expected);
        }

        [Theory]
        [InlineData("Or", AccessCombine.Or)]
        [InlineData("And", AccessCombine.And)]
        public void The_access_combine_arrives_as_the_name_the_client_sends(string wire, AccessCombine expected)
        {
            // Same defect, same request, one field along — and not yet reported only because the
            // "Restrict further" toggle is reached less often than the method tabs.
            var json = """{"functionId":"fn-1","trigger":{"combine":"WIRE"}}""".Replace("WIRE", wire, StringComparison.Ordinal);

            JsonSerializer.Deserialize<SaveFunctionRequestDto>(json, Web)!
                .Trigger.Combine.Should().Be(expected);
        }

        [Fact]
        public void A_stored_trigger_written_as_a_number_still_reads()
        {
            // JsonStringEnumConverter accepts numbers on the way in, so nothing already persisted
            // or already in flight from an older client stops working.
            var json = """{"functionId":"fn-1","trigger":{"httpMethod":1,"combine":1}}""";

            var trigger = JsonSerializer.Deserialize<SaveFunctionRequestDto>(json, Web)!.Trigger;

            trigger.HttpMethod.Should().Be(HttpTriggerMethod.Get);
            trigger.Combine.Should().Be(AccessCombine.And);
        }

        // ---- create validation -------------------------------------------------------

        private static CreateFunctionRequestDto Create(
            string name = "A function", string? description = null, string? template = null) =>
            new() { Name = name, Description = description, Template = template };

        [Theory]
        [InlineData("")]
        [InlineData("a")]
        public void A_name_the_create_dialog_would_refuse_is_refused_here_too(string name)
        {
            // The dialog enforces 2..64 in zod; the API took 1..200, so anything not typed into
            // that dialog was held to a different contract from everything the product shows.
            new CreateFunctionRequestValidator().Validate(Create(name: name))
                .IsValid.Should().BeFalse();
        }

        [Fact]
        public void A_name_over_sixty_four_characters_is_refused()
        {
            new CreateFunctionRequestValidator().Validate(Create(name: new string('x', 65)))
                .IsValid.Should().BeFalse();

            new CreateFunctionRequestValidator().Validate(Create(name: new string('x', 64)))
                .IsValid.Should().BeTrue();
        }

        [Fact]
        public void A_description_over_two_hundred_characters_is_refused()
        {
            new CreateFunctionRequestValidator().Validate(Create(description: new string('x', 201)))
                .IsValid.Should().BeFalse();

            new CreateFunctionRequestValidator().Validate(Create(description: new string('x', 200)))
                .IsValid.Should().BeTrue();
        }

        [Fact]
        public void An_unknown_template_is_refused_rather_than_quietly_becoming_the_minimal_one()
        {
            // "Nope" used to create a working Minimal function with no error and no indication,
            // so a typo in a template name was indistinguishable from asking for Minimal.
            var result = new CreateFunctionRequestValidator().Validate(Create(template: "Nope"));

            result.IsValid.Should().BeFalse();
            result.Errors.Should().ContainSingle()
                .Which.ErrorMessage.Should().Contain("FetchTransform");
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData(FunctionStarterTemplates.Minimal)]
        [InlineData(FunctionStarterTemplates.HttpEcho)]
        [InlineData(FunctionStarterTemplates.FetchTransform)]
        public void Every_template_the_dialog_offers_is_accepted_and_so_is_none(string? template)
        {
            new CreateFunctionRequestValidator().Validate(Create(template: template))
                .IsValid.Should().BeTrue();
        }

        [Fact]
        public void Update_holds_a_rename_to_the_same_name_bounds_as_create()
        {
            var validator = new UpdateFunctionRequestValidator();

            validator.Validate(new UpdateFunctionRequestDto { FunctionId = "fn-1", Name = "a" })
                .IsValid.Should().BeFalse();
            validator.Validate(new UpdateFunctionRequestDto { FunctionId = "fn-1", Name = "ab" })
                .IsValid.Should().BeTrue();
        }

        [Fact]
        public void A_rename_does_not_trip_over_a_description_that_predates_the_shorter_bound()
        {
            // The detail page has no description field: the rename sends the stored value back
            // unchanged. Holding that pass-through to the create bound would make a function whose
            // description came from a direct API call impossible to rename.
            new UpdateFunctionRequestValidator().Validate(new UpdateFunctionRequestDto
            {
                FunctionId = "fn-1",
                Name = "Still renameable",
                Description = new string('x', 400),
            }).IsValid.Should().BeTrue();
        }

        // ---- how a refusal reaches the caller ----------------------------------------

        private static ExceptionContext ContextFor(Exception exception) =>
            new(
                new ActionContext(new DefaultHttpContext(), new RouteData(), new ActionDescriptor()),
                [])
            { Exception = exception };

        [Theory]
        [InlineData(typeof(FunctionValidationException), 400)]
        [InlineData(typeof(FunctionAuthorizationException), 401)]
        [InlineData(typeof(FunctionForbiddenException), 403)]
        [InlineData(typeof(FunctionNotFoundException), 404)]
        [InlineData(typeof(FunctionRequestTooLargeException), 413)]
        public void A_management_refusal_is_its_own_status_code_and_not_a_500(Type exceptionType, int expected)
        {
            // Create, Update, Save, Deploy and Rollback return their DTO directly rather than an
            // ActionResult, so nothing mapped these and every one of them surfaced as a bare 500
            // — which tells a client to retry something that will fail identically every time.
            var context = ContextFor((Exception)Activator.CreateInstance(exceptionType, "nope")!);

            new FunctionExceptionFilter().OnException(context);

            context.ExceptionHandled.Should().BeTrue();
            context.Result.Should().BeOfType<ObjectResult>()
                .Which.StatusCode.Should().Be(expected);
        }

        [Fact]
        public void A_method_refusal_still_names_the_method_that_would_work()
        {
            var context = ContextFor(new FunctionMethodNotAllowedException("POST"));

            new FunctionExceptionFilter().OnException(context);

            context.Result.Should().BeOfType<ObjectResult>().Which.StatusCode.Should().Be(405);
            context.HttpContext.Response.Headers.Allow.ToString().Should().Be("POST");
        }

        [Fact]
        public void A_rate_limit_refusal_still_says_how_long_to_wait()
        {
            var context = ContextFor(new FunctionRateLimitedException("slow down", 42));

            new FunctionExceptionFilter().OnException(context);

            context.Result.Should().BeOfType<ObjectResult>().Which.StatusCode.Should().Be(429);
            context.HttpContext.Response.Headers.RetryAfter.ToString().Should().Be("42");
        }

        [Fact]
        public void Anything_this_filter_does_not_know_is_left_to_fail_as_it_would_have()
        {
            // A genuine fault must keep its 500 and its logging; swallowing everything here would
            // turn a bug into a tidy 400.
            var context = ContextFor(new InvalidOperationException("something else entirely"));

            new FunctionExceptionFilter().OnException(context);

            context.ExceptionHandled.Should().BeFalse();
            context.Result.Should().BeNull();
        }
    }
}
