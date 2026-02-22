using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace AIDev.Api.Auth;

/// <summary>
/// Requires a valid X-Dev-Key header matching the configured DevOps:ApiKey value.
/// Use this attribute on controllers or actions that should be accessible to
/// automated agents and tooling without Entra ID auth.
/// Returns 401 if the key is missing, 403 if it doesn't match.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public class DevApiKeyAttribute : Attribute, IAsyncActionFilter
{
    private const string HeaderName = "X-Dev-Key";
    private const string ConfigKey = "DevOps:ApiKey";

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var configuration = context.HttpContext.RequestServices.GetRequiredService<IConfiguration>();
        var configuredKey = configuration[ConfigKey];

        // If no key is configured, the DevOps endpoints are disabled entirely
        if (string.IsNullOrWhiteSpace(configuredKey))
        {
            context.Result = new ObjectResult(new { error = "DevOps API is not configured on this environment" })
            {
                StatusCode = 503
            };
            return;
        }

        if (!context.HttpContext.Request.Headers.TryGetValue(HeaderName, out var providedKey))
        {
            context.Result = new UnauthorizedObjectResult(new { error = $"Missing {HeaderName} header" });
            return;
        }

        if (!string.Equals(configuredKey, providedKey.ToString(), StringComparison.Ordinal))
        {
            context.Result = new ObjectResult(new { error = "Invalid API key" })
            {
                StatusCode = 403
            };
            return;
        }

        await next();
    }
}
