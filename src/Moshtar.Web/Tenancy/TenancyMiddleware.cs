using Moshtar.Application.Tenancy;

namespace Moshtar.Web.Tenancy;

internal static class TenancyMiddleware
{
    /// <summary>Requests voor een onbekende domeinnaam krijgen een 404.</summary>
    public static IApplicationBuilder UseTenantResolution(this IApplicationBuilder app) =>
        app.Use(async (context, next) =>
        {
            var tenantContext = context.RequestServices.GetRequiredService<ITenantContext>();
            if (tenantContext.Tenant is null)
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                await context.Response.WriteAsync("Onbekende verhuurder.");
                return;
            }
            await next();
        });
}
