using System.Globalization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Moshtar.Application.Tenancy;
using Moshtar.Infrastructure;
using Moshtar.Infrastructure.Identity;
using Moshtar.Infrastructure.Persistence;
using Moshtar.Web.Components;
using Moshtar.Web.Identity;
using Moshtar.Web.Tenancy;
using MudBlazor.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
builder.Services.AddMudServices();

builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<HostTenantContext>();
builder.Services.AddScoped<ITenantContext>(sp => sp.GetRequiredService<HostTenantContext>());
builder.Services.AddBackOfficeIdentity();

builder.Services.AddLocalization(o => o.ResourcesPath = "Resources");
string[] cultures = ["nl", "fr", "en"];
builder.Services.Configure<RequestLocalizationOptions>(o =>
{
    o.SetDefaultCulture("nl").AddSupportedCultures(cultures).AddSupportedUICultures(cultures);
    o.ApplyCurrentCultureToResponseHeaders = true;
});

var app = builder.Build();

// Installatie: `beheerder-aanmaken <verhuurder> <e-mail>` maakt de eerste Beheerder van een verhuurder.
// Het wachtwoord komt uit MOSHTAR_WACHTWOORD of wordt gevraagd, zodat het niet in de shellgeschiedenis belandt.
if (args is ["beheerder-aanmaken", var tenantSlug, var adminEmail, ..])
{
    var password = Environment.GetEnvironmentVariable("MOSHTAR_WACHTWOORD");
    if (string.IsNullOrEmpty(password))
    {
        Console.Write("Wachtwoord (minstens 12 tekens): ");
        password = Console.ReadLine() ?? "";
    }
    await using var scope = app.Services.CreateAsyncScope();
    try
    {
        await BackOfficeUsers.EnsureAsync(scope.ServiceProvider, tenantSlug, adminEmail, password, UserRole.Administrator);
        Console.WriteLine($"Beheerder {adminEmail} staat klaar bij verhuurder '{tenantSlug}'.");
        return 0;
    }
    catch (InvalidOperationException e)
    {
        Console.Error.WriteLine(e.Message);
        return 1;
    }
}

if (app.Environment.IsDevelopment())
{
    // Lokaal: database automatisch bijwerken en demo-data voorzien.
    await using var scope = app.Services.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.MigrateAsync();
    await DbSeeder.SeedAsync(db);
    foreach (var demo in app.Configuration.GetSection("DemoGebruikers").GetChildren())
        await BackOfficeUsers.EnsureAsync(scope.ServiceProvider, demo["Verhuurder"]!, demo["Email"]!, demo["Wachtwoord"]!, Enum.Parse<UserRole>(demo["Rol"]!));
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

app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

app.MapPost(IdentitySetup.LogoutPath, async (SignInManager<User> signInManager, [FromForm] string? returnUrl) =>
{
    await signInManager.SignOutAsync();
    return Results.LocalRedirect(IdentitySetup.LoginPath);
});

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
return 0;

public partial class Program;
