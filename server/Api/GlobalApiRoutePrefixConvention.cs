using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApplicationModels;

namespace BlocksTemplate.Api;

/// <summary>Prepends a segment (e.g. <c>api</c>) to every controller’s attribute route template.</summary>
internal sealed class GlobalApiRoutePrefixConvention(string routeTemplate) : IApplicationModelConvention
{
    private readonly AttributeRouteModel _prefix = new(new RouteAttribute(routeTemplate));

    public void Apply(ApplicationModel application)
    {
        foreach (var controller in application.Controllers)
        {
            foreach (var selector in controller.Selectors)
            {
                if (selector.AttributeRouteModel is null)
                {
                    continue;
                }

                // A controller that already declares an explicit "api/..." route opts out of the prefix so its
                // effective path is not double-prefixed to "api/api/...". Prefer "[controller]/[action]" and
                // let this convention own the prefix; no controller currently needs the escape hatch.
                var template = selector.AttributeRouteModel.Template ?? string.Empty;
                if (template.Equals("api", StringComparison.OrdinalIgnoreCase)
                    || template.StartsWith("api/", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                selector.AttributeRouteModel = AttributeRouteModel.CombineAttributeRouteModel(
                    _prefix,
                    selector.AttributeRouteModel);
            }
        }
    }
}
