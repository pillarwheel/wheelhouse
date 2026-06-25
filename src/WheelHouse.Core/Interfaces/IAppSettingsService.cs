using WheelHouse.Core.Models;

namespace WheelHouse.Core.Interfaces;

/// <summary>Reads and writes the single application configuration row (branding + defaults).</summary>
public interface IAppSettingsService
{
    /// <summary>Returns the configuration, creating the default row on first access.</summary>
    Task<AppConfiguration> GetAsync(CancellationToken cancellationToken = default);

    /// <summary>Persists changes to the configuration.</summary>
    Task SaveAsync(AppConfiguration config, CancellationToken cancellationToken = default);
}
