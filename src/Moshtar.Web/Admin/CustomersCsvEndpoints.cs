using Microsoft.AspNetCore.Authorization;
using Moshtar.Application.Customers;
using Moshtar.Web.Identity;

namespace Moshtar.Web.Admin;

/// <summary>Het klantenbestand als CSV en het voorbeeldbestand voor een import, alleen voor de beheerder.</summary>
public static class CustomersCsvEndpoints
{
    public const string ExportPath = "/admin/klanten.csv";
    public const string SamplePath = "/admin/klanten-voorbeeld.csv";

    public static IEndpointRouteBuilder MapCustomersCsv(this IEndpointRouteBuilder app)
    {
        var administrator = new AuthorizeAttribute { Roles = Roles.Administrator };
        app.MapGet(ExportPath, async (ICustomerTransfer transfer, HttpContext context) =>
                Results.File(CustomerCsv.Bytes(await transfer.ExportAsync(context.RequestAborted)), "text/csv; charset=utf-8", "klanten.csv"))
            .RequireAuthorization(administrator);
        app.MapGet(SamplePath, () => Results.File(CustomerCsv.Bytes(CustomerCsv.Sample()), "text/csv; charset=utf-8", "klanten-voorbeeld.csv"))
            .RequireAuthorization(administrator);
        return app;
    }
}
