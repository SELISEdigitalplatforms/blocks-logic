using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Workflow.DomainService.Services;

namespace BlocksTemplate.Api.Security;

/// <summary>
/// Webhook routes accept up to 50 MiB of files (SPEC8). Kestrel's body limit is raised for them only, and early
/// in the pipeline: the authentication middleware may read the form (Genesis resolving the tenant for a bearer
/// token) before the action's <c>[RequestSizeLimit]</c> applies. Every other route keeps the default.
/// </summary>
public static class WebhookBodySizeLimit
{
    public static bool IsWebhookPath(PathString path) =>
        path.StartsWithSegments("/api/Workflow/Webhook", StringComparison.OrdinalIgnoreCase)
        || path.StartsWithSegments("/api/Workflow/webhook-test", StringComparison.OrdinalIgnoreCase);

    /// <summary>Raises the request's body limit when it targets a webhook route and the limit can still change.</summary>
    public static void Apply(HttpContext context)
    {
        if (!IsWebhookPath(context.Request.Path)) return;

        var bodySize = context.Features.Get<IHttpMaxRequestBodySizeFeature>();
        if (bodySize is { IsReadOnly: false })
        {
            bodySize.MaxRequestBodySize = WebhookIntakeLimits.RequestSizeLimitBytes;
        }
    }
}
