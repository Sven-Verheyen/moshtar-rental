using System.Globalization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Moshtar.Application.Tenancy;
using Moshtar.Infrastructure;
using Moshtar.Infrastructure.Identity;
using Moshtar.Infrastructure.Persistence;
using Moshtar.Web.Components;
using Moshtar.Web.Identity;
using Moshtar.Web.Site;
using Moshtar.Web.Tenancy;
using MudBlazor.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
builder.Services.AddMudServices();

builder.Services.AddInfrastructure(builder.Configuration,
    sendRealMail: !builder.Environment.IsDevelopment() && !builder.Environment.IsEnvironment("Testing"));
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<HostTenantContext>();
builder.Services.AddScoped<ITenantContext>(sp => sp.GetRequiredService<HostTenantContext>());
builder.Services.AddBackOfficeIdentity();

builder.Services.AddLocalization(o => o.ResourcesPath = "Resources");
builder.Services.AddScoped<SiteLinks>();

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
app.UseTenantResolution();
app.UsePrimaryHostRedirect();
// De taal staat in de URL (ADR 0004); daarna pas routeren, want het taalvoorvoegsel wordt de PathBase.
app.UseSiteLanguage();
app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

app.MapPost(IdentitySetup.LogoutPath, async (SignInManager<User> signInManager, [FromForm] string? returnUrl) =>
{
    await signInManager.SignOutAsync();
    return Results.LocalRedirect(IdentitySetup.LoginPath);
});

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
return 0;

public partial class Program;
