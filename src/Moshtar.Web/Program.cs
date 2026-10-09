using System.Globalization;
using Microsoft.AspNetCore.Localization;
using Microsoft.EntityFrameworkCore;
using Moshtar.Application.Tenancy;
using Moshtar.Infrastructure;
using Moshtar.Infrastructure.Persistence;
using Moshtar.Web.Components;
using Moshtar.Web.Tenancy;
using MudBlazor.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
builder.Services.AddMudServices();

builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ITenantContext, HostTenantContext>();

builder.Services.AddLocalization(o => o.ResourcesPath = "Resources");
string[] cultures = ["nl", "fr", "en"];
builder.Services.Configure<RequestLocalizationOptions>(o =>
{
    o.SetDefaultCulture("nl").AddSupportedCultures(cultures).AddSupportedUICultures(cultures);
    o.ApplyCurrentCultureToResponseHeaders = true;
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    // Lokaal: database automatisch bijwerken en demo-data voorzien.
    await using var scope = app.Services.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.MigrateAsync();
    await DbSeeder.SeedAsync(db);
}
else
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();
app.UseRequestLocalization();
app.UseTenantResolution();

// Het back office heeft nog geen login: buiten ontwikkeling is het voorlopig onbereikbaar.
if (!app.Environment.IsDevelopment())
{
    app.Use(async (context, next) =>
    {
        if (context.Request.Path.StartsWithSegments("/admin"))
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }
        await next();
    });
}

app.UseAntiforgery();

app.MapGet("/culture/{culture}", (string culture, string? redirectUri, HttpContext context) =>
{
    if (cultures.Contains(culture))
    {
        context.Response.Cookies.Append(
            CookieRequestCultureProvider.DefaultCookieName,
            CookieRequestCultureProvider.MakeCookieValue(new RequestCulture(culture)),
            new CookieOptions { Expires = DateTimeOffset.UtcNow.AddYears(1), IsEssential = true, SameSite = SameSiteMode.Lax });
    }
    return Results.LocalRedirect(redirectUri is { Length: > 0 } && redirectUri.StartsWith('/') && !redirectUri.StartsWith("//") ? redirectUri : "/");
});

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
