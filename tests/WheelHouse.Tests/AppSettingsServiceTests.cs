using Microsoft.EntityFrameworkCore;
using WheelHouse.Core.Models;
using WheelHouse.Infrastructure.Persistence;
using WheelHouse.Infrastructure.Services;
using Xunit;

namespace WheelHouse.Tests;

public class AppSettingsServiceTests
{
    private static WheelHouseDbContext NewDb()
    {
        var options = new DbContextOptionsBuilder<WheelHouseDbContext>()
            .UseSqlite($"Data Source=file:{Guid.NewGuid():N}?mode=memory&cache=shared")
            .Options;
        var db = new WheelHouseDbContext(options);
        db.Database.OpenConnection();
        db.Database.EnsureCreated();
        return db;
    }

    [Fact]
    public async Task Get_Creates_Default_Row()
    {
        using var db = NewDb();
        var svc = new AppSettingsService(db);

        var config = await svc.GetAsync();

        Assert.Equal(1, config.Id);
        Assert.Equal("Your Studio", config.CompanyName);
        Assert.Equal("WheelHouse", config.ProductName);
        Assert.Equal("acceptEdits", config.DefaultPermissionMode);
        Assert.Equal(1, await db.AppConfig.CountAsync());
    }

    [Fact]
    public async Task Save_Persists_And_Stays_Single_Row()
    {
        using var db = NewDb();
        var svc = new AppSettingsService(db);
        await svc.GetAsync(); // create default

        await svc.SaveAsync(new AppConfiguration
        {
            Id = 1,
            CompanyName = "Pillarwheel Studio",
            ProductName = "WheelHouse",
            Tagline = "Custom tagline",
            DefaultPermissionMode = "bypassPermissions"
        });

        var reloaded = await new AppSettingsService(db).GetAsync();
        Assert.Equal("Pillarwheel Studio", reloaded.CompanyName);
        Assert.Equal("Custom tagline", reloaded.Tagline);
        Assert.Equal("bypassPermissions", reloaded.DefaultPermissionMode);
        Assert.Equal(1, await db.AppConfig.CountAsync()); // never duplicates
    }
}
