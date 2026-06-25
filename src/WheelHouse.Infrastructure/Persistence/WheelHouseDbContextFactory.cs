using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace WheelHouse.Infrastructure.Persistence;

/// <summary>
/// Design-time factory so the EF tools (<c>dotnet ef migrations …</c>) can construct the
/// context without booting the full application/DI graph.
/// </summary>
public class WheelHouseDbContextFactory : IDesignTimeDbContextFactory<WheelHouseDbContext>
{
    public WheelHouseDbContext CreateDbContext(string[] args)
    {
        var path = DependencyInjection.DefaultDatabasePath();
        var options = new DbContextOptionsBuilder<WheelHouseDbContext>()
            .UseSqlite($"Data Source={path}")
            .Options;
        return new WheelHouseDbContext(options);
    }
}
