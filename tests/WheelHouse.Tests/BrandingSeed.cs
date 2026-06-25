using Microsoft.EntityFrameworkCore;
using WheelHouse.Infrastructure;
using WheelHouse.Infrastructure.Persistence;
using WheelHouse.Infrastructure.Services;
using Xunit;

namespace WheelHouse.Tests;

/// <summary>Gated helper (WHEELHOUSE_SEED=1) that sets custom branding in the real app DB,
/// so the sidebar render can be verified in the browser.</summary>
public class BrandingSeed
{
    [Fact]
    public async Task SetCompanyName()
    {
        if (Environment.GetEnvironmentVariable("WHEELHOUSE_SEED") is not ("1" or "true")) return;

        var options = new DbContextOptionsBuilder<WheelHouseDbContext>()
            .UseSqlite($"Data Source={DependencyInjection.DefaultDatabasePath()}")
            .Options;
        using var db = new WheelHouseDbContext(options);

        var svc = new AppSettingsService(db);
        var config = await svc.GetAsync();
        config.CompanyName = "Pillarwheel Studio";
        config.Tagline = "Coding research, development & implementation";
        await svc.SaveAsync(config);
    }
}
