using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.DependencyInjection;
using Photino.NET;
using WheelHouse.Core.Interfaces;
using WheelHouse.Web;

namespace WheelHouse.Desktop;

/// <summary>
/// Desktop shell: boots the in-process ASP.NET Core + Blazor host on a free local port,
/// then opens a native Photino window pointed at it.
/// </summary>
internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        var port = GetFreePort();
        var url = $"http://127.0.0.1:{port}";

        var app = WheelHouseWebApp.Build(args, url);

        // Start the web host and block until it is listening.
        app.StartAsync().GetAwaiter().GetResult();

        // Title the native window from the configured branding.
        var title = "WheelHouse";
        using (var scope = app.Services.CreateScope())
        {
            var config = scope.ServiceProvider.GetRequiredService<IAppSettingsService>()
                .GetAsync().GetAwaiter().GetResult();
            title = $"{config.ProductName} — {config.CompanyName}";
        }

        var window = new PhotinoWindow()
            .SetTitle(title)
            .SetUseOsDefaultSize(false)
            .SetSize(1440, 920)
            .SetResizable(true)
            .Center()
            .Load(url);

        window.WaitForClose();

        app.StopAsync().GetAwaiter().GetResult();
    }

    /// <summary>Asks the OS for an available TCP port by binding to port 0.</summary>
    private static int GetFreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}
