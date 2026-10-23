using System.IO;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using NetPulse.App.ViewModels;
using NetPulse.Application.Ports;
using NetPulse.Application.UseCases;
using NetPulse.Infrastructure.Windows.Network;
using NetPulse.Persistence;

namespace NetPulse.App;

public partial class App : System.Windows.Application
{
    private ServiceProvider? _serviceProvider;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var services = new ServiceCollection();

        var dbPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "NetPulseOptimizer",
            "netpulse.db");

        services.AddSingleton<INetworkAdapterInventoryService, NetworkAdapterInventoryService>();
        services.AddSingleton<ILatencyProbeService, IcmpLatencyProbeService>();
        services.AddSingleton<IDiagnosticHistoryRepository>(_ => new SqliteDiagnosticHistoryRepository(dbPath));
        services.AddSingleton<ConnectionDiagnosticUseCase>();
        services.AddSingleton<DashboardViewModel>();
        services.AddSingleton<MainWindow>();

        _serviceProvider = services.BuildServiceProvider();

        var mainWindow = _serviceProvider.GetRequiredService<MainWindow>();
        mainWindow.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _serviceProvider?.Dispose();
        base.OnExit(e);
    }
}
