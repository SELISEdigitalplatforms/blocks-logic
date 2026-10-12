using Blocks.Genesis;
using Functions.DomainService.Utils;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace BlocksTemplate.Api;

/// <summary>
/// Maps the function domain's exceptions onto HTTP status codes for the <b>management</b>
/// surface — everything the Studio calls at <c>/api/functions/…</c>.
/// </summary>
/// <remarks>
/// The public invocation surface does its own mapping inline, because it answers in the
/// invoke error envelope (<c>{ error: { code, message } }</c>) that non-Blocks callers see, not
/// in <see cref="BaseResponse"/>. That path catches every one of these before they reach a
/// filter, so this only ever fires for the management actions.
/// <para>
/// Those actions return their DTO directly rather than an <see cref="ActionResult"/>, so
/// before this existed a validation failure — an empty name, a note over its length, a rollback
/// to a version that is not there — left the domain exception unhandled and the caller got a
/// bare 500. A 500 tells a client to retry something that will fail identically every time.
/// </para>
/// <para>
/// Modelled on <c>SecretExceptionFilter</c>, and deliberately the same response shape: the
/// client's error handling reads <c>errors</c> off the body and shows it, whichever endpoint
/// produced it.
/// </para>
/// </remarks>
public sealed class FunctionExceptionFilter : IExceptionFilter
{
    public void OnException(ExceptionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var (statusCode, errorKey) = context.Exception switch
        {
            FunctionValidationException => (StatusCodes.Status400BadRequest, "invalid_request"),
            FunctionAuthorizationException => (StatusCodes.Status401Unauthorized, "unauthorized"),
            FunctionForbiddenException => (StatusCodes.Status403Forbidden, "forbidden"),
            FunctionNotFoundException => (StatusCodes.Status404NotFound, "not_found"),
            FunctionMethodNotAllowedException => (StatusCodes.Status405MethodNotAllowed, "method_not_allowed"),
            FunctionRequestTooLargeException => (StatusCodes.Status413PayloadTooLarge, "request_too_large"),
            FunctionRateLimitedException => (StatusCodes.Status429TooManyRequests, "rate_limited"),
            // Nothing ran and the same request is expected to work shortly, which is a 503 —
            // distinct from the 500 an unexpected fault still gets.
            FunctionUnavailableException => (StatusCodes.Status503ServiceUnavailable, "unavailable"),
            _ => (0, string.Empty),
        };

        if (statusCode == 0)
        {
            return;
        }

        // The headers that make these codes actionable, exactly as the invoke surface sets them.
        switch (context.Exception)
        {
            case FunctionMethodNotAllowedException methodNotAllowed:
                context.HttpContext.Response.Headers.Allow = methodNotAllowed.Allowed;
                break;
            case FunctionRateLimitedException rateLimited:
                context.HttpContext.Response.Headers.RetryAfter =
                    rateLimited.RetryAfterSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture);
                break;
            case FunctionUnavailableException unavailable:
                context.HttpContext.Response.Headers.RetryAfter =
                    unavailable.RetryAfterSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture);
                break;
        }

        var errors = new Dictionary<string, string> { [errorKey] = context.Exception.Message };
        // Not a message: the editor takes it out of the errors and opens that build's log.
        if (context.Exception is FunctionBuildFailedException buildFailed && !string.IsNullOrEmpty(buildFailed.BuildId))
        {
            errors["buildId"] = buildFailed.BuildId;
        }
        // Not messages either: the delete dialog reads them to offer "delete anyway" with what it will do.
        if (context.Exception is FunctionDeleteBlockedException blocked)
        {
            errors["deleteBlocked"] = "true";
            errors["activeRuns"] = blocked.ActiveRuns.ToString(System.Globalization.CultureInfo.InvariantCulture);
            errors["workflows"] = blocked.Workflows.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        context.Result = new ObjectResult(new BaseResponse
        {
            IsSuccess = false,
            Errors = errors,
        })
        {
            StatusCode = statusCode,
        };

        context.ExceptionHandled = true;
    }
}
