using System.Net;
using Moshtar.Application.Tenancy;

namespace Moshtar.Web.Tenancy;

internal static class PrimaryHostRedirect
{
    /// <summary>
    /// Een request op een ander domein van de verhuurder gaat met een 301 naar hetzelfde pad op zijn hoofddomein,
    /// zodat zoekmachines geen dubbele inhoud zien. Lokaal (localhost) wordt nooit doorgestuurd.
    /// </summary>
    public static IApplicationBuilder UsePrimaryHostRedirect(this IApplicationBuilder app) =>
        app.Use(async (context, next) =>
        {
            var host = context.Request.Host.Host;
            var primary = context.RequestServices.GetRequiredService<ITenantContext>().Tenant?.PrimaryHost;
            if (primary is not null && !string.Equals(host, primary, StringComparison.OrdinalIgnoreCase) && !IsLocal(host))
            {
                var request = context.Request;
                context.Response.Redirect($"{request.Scheme}://{primary}{request.PathBase}{request.Path}{request.QueryString}", permanent: true);
                return;
            }
            await next();
        });

    private static bool IsLocal(string host) =>
        host.Equals("localhost", StringComparison.OrdinalIgnoreCase) || (IPAddress.TryParse(host, out var ip) && IPAddress.IsLoopback(ip));
}
