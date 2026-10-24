using Blocks.Genesis;
using DomainService.Notification;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;

namespace BlocksTemplate.Api.Security;

/// <summary>Reads the caller's bearer token for the notifier send permission.</summary>
public interface INotifierSendPermission
{
    /// <summary>What the token of <paramref name="httpContext"/> says about <see cref="NotifierSendPermission.Resource"/>.</summary>
    Task<NotifierCallerToken> CheckAsync(HttpContext httpContext);
}

/// <summary>
/// The token path of the notifier send actions. The permission is resolved by Genesis's own <c>Protected</c>
/// policy (the one behind every <c>[ProtectedEndPoint]</c>): a <c>Permissions</c> row for the resource in the
/// caller's tenant and organization whose roles match the caller's, or the resource in the token's permission
/// claims. The send actions carry no <c>[ProtectedEndPoint]</c> (the secret path must stay open), so the
/// resource name goes in through the <c>HttpContext.Items</c> key that policy reads.
/// </summary>
public sealed class NotifierSendPermission : INotifierSendPermission
{
    /// <summary>The permission a token needs to send a notification.</summary>
    public const string Resource = "blocks-logic::notifier::send";

    /// <summary>Genesis's permission policy (<c>ProtectedEndPointAttribute.Policy</c>).</summary>
    public const string ProtectedPolicy = "Protected";

    /// <summary>
    /// The <c>HttpContext.Items</c> key the Genesis permission handler reads the resource from
    /// (<c>BlocksConstants.ProtectedResourceName</c>, internal in Genesis).
    /// </summary>
    public const string ResourceItemKey = "ProtectedResourceName";

    private readonly IAuthorizationService _authorization;
    private readonly ITenants _tenants;
    private readonly ILogger<NotifierSendPermission> _logger;

    /// <summary>Takes the authorization service, the tenant list (to find a cookie token) and a logger.</summary>
    public NotifierSendPermission(IAuthorizationService authorization, ITenants tenants,
        ILogger<NotifierSendPermission> logger)
    {
        _authorization = authorization;
        _tenants = tenants;
        _logger = logger;
    }

    /// <inheritdoc />
    public Task<NotifierCallerToken> CheckAsync(HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        return CheckCoreAsync(httpContext);
    }

    private async Task<NotifierCallerToken> CheckCoreAsync(HttpContext httpContext)
    {
        // Authentication already ran (NotifierOptionalTokenAttribute): an unauthenticated user with a token
        // means the token failed validation.
        if (httpContext.User.Identity?.IsAuthenticated != true)
            return HasToken(httpContext.Request) ? NotifierCallerToken.Invalid : NotifierCallerToken.None;

        httpContext.Items[ResourceItemKey] = Resource;
        try
        {
            var result = await _authorization.AuthorizeAsync(httpContext.User, httpContext, ProtectedPolicy);
            return result.Succeeded ? NotifierCallerToken.Permitted : NotifierCallerToken.NoPermission;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A failed lookup refuses the token path; it must not turn into a 500. The secret path is unaffected.
            _logger.LogWarning(ex, "Notifier send permission check failed; the token is treated as without permission");
            return NotifierCallerToken.NoPermission;
        }
        finally
        {
            httpContext.Items.Remove(ResourceItemKey);
        }
    }

    private bool HasToken(HttpRequest request)
    {
        try
        {
            return !string.IsNullOrWhiteSpace(TokenHelper.GetToken(request, _tenants).Token);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Could not read the cookie/header: count it as a token that is not valid (refused, never a 500).
            _logger.LogWarning(ex, "Notifier send could not read the caller token");
            return true;
        }
    }
}

/// <summary>
/// Makes Genesis validate a bearer token on an action that must stay open without one. Genesis parses the token
/// only on endpoints with authorization metadata; this attribute adds it with a policy that every caller meets,
/// so a call without a token still reaches the action (which then checks the secret or the permission itself).
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class NotifierOptionalTokenAttribute : Attribute, IAuthorizeData
{
    /// <summary>The policy name; registered by <see cref="NotifierSendAuthorization.AddNotifierSendPolicy"/>.</summary>
    public const string PolicyName = "NotifierOptionalToken";

    /// <inheritdoc />
    public string? Policy { get; set; } = PolicyName;

    /// <inheritdoc />
    public string? Roles { get; set; }

    /// <inheritdoc />
    public string? AuthenticationSchemes { get; set; }
}

/// <summary>Service registration for the notifier send token path.</summary>
public static class NotifierSendAuthorization
{
    /// <summary>Registers <see cref="INotifierSendPermission"/> and the open policy of <see cref="NotifierOptionalTokenAttribute"/>.</summary>
    public static IServiceCollection AddNotifierSendPolicy(this IServiceCollection services)
    {
        services.AddScoped<INotifierSendPermission, NotifierSendPermission>();
        services.AddAuthorizationBuilder()
            .AddPolicy(NotifierOptionalTokenAttribute.PolicyName, policy => policy.RequireAssertion(_ => true));
        return services;
    }
}
