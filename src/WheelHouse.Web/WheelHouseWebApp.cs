using Microsoft.EntityFrameworkCore;
using WheelHouse.Core.Interfaces;
using WheelHouse.Infrastructure;
using WheelHouse.Infrastructure.Configuration;
using WheelHouse.Infrastructure.Persistence;

namespace WheelHouse.Web;

/// <summary>
/// Builds the shared ASP.NET Core + Blazor Server host. Reused by both the standalone
/// web entry point and the Photino desktop shell so configuration lives in one place.
/// </summary>
public static class WheelHouseWebApp
{
    public static WebApplication Build(string[] args, string? urls = null)
    {
        // Load .env before anything reads environment variables (options do so at registration).
        var envCount = EnvFile.Load();

        var builder = WebApplication.CreateBuilder(args);
        if (urls is not null) builder.WebHost.UseUrls(urls);

        builder.Services.AddRazorComponents().AddInteractiveServerComponents();
        builder.Services.AddWheelHouseInfrastructure();

        var app = builder.Build();

        if (!app.Environment.IsDevelopment())
            app.UseExceptionHandler("/Error", createScopeForErrors: true);

        app.UseStaticFiles();
        app.UseAntiforgery();
        app.MapRazorComponents<Components.App>().AddInteractiveServerRenderMode();

        if (envCount > 0)
            app.Logger.LogInformation("Loaded {Count} variable(s) from .env", envCount);

        EnsureDatabase(app);
        return app;
    }

    private static void EnsureDatabase(WebApplication app)
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WheelHouseDbContext>();
        db.Database.Migrate();

        // Seed the built-in prompt-template library (idempotent).
        var templates = scope.ServiceProvider.GetRequiredService<IPromptTemplateService>();
        templates.SeedBuiltInsAsync().GetAwaiter().GetResult();
    }
}
