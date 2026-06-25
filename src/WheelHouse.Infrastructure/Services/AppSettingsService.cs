using Microsoft.EntityFrameworkCore;
using WheelHouse.Core.Interfaces;
using WheelHouse.Core.Models;
using WheelHouse.Infrastructure.Persistence;

namespace WheelHouse.Infrastructure.Services;

/// <summary>EF-backed application configuration store (single row, id = 1).</summary>
public class AppSettingsService : IAppSettingsService
{
    private readonly WheelHouseDbContext _db;

    public AppSettingsService(WheelHouseDbContext db) => _db = db;

    public async Task<AppConfiguration> GetAsync(CancellationToken cancellationToken = default)
    {
        var config = await _db.AppConfig.FirstOrDefaultAsync(c => c.Id == 1, cancellationToken);
        if (config is null)
        {
            config = new AppConfiguration { Id = 1 };
            _db.AppConfig.Add(config);
            await _db.SaveChangesAsync(cancellationToken);
        }
        return config;
    }

    public async Task SaveAsync(AppConfiguration config, CancellationToken cancellationToken = default)
    {
        var tracked = await _db.AppConfig.FirstOrDefaultAsync(c => c.Id == 1, cancellationToken);
        if (tracked is null)
        {
            config.Id = 1;
            _db.AppConfig.Add(config);
        }
        else
        {
            tracked.CompanyName = config.CompanyName;
            tracked.ProductName = config.ProductName;
            tracked.Tagline = config.Tagline;
            tracked.DefaultPermissionMode = config.DefaultPermissionMode;
        }
        await _db.SaveChangesAsync(cancellationToken);
    }
}
