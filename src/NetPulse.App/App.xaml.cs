using System.IO;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using NetPulse.App.ViewModels;
using NetPulse.Application.Ports;
using NetPulse.Application.UseCases;
using NetPulse.Domain.Recommendations;
using NetPulse.Infrastructure.Windows.Network;
using NetPulse.Persistence;
using NetPulse.PrivilegedAgent;

namespace NetPulse.App;

public partial class App : System.Windows.Application
{
    private ServiceProvider? _serviceProvider;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Defense in depth: LoadAdaptersAsync and RunDiagnosticAsync already catch their own
        // exceptions and show a status message, but this is the backstop for anything that
        // doesn't - without it, any uncaught exception on the UI thread crashes the whole
        // app silently instead of telling the user what happened.
        DispatcherUnhandledException += (_, args) =>
        {
            MessageBox.Show(
                $"Ocurrió un error inesperado y se capturó antes de que pudiera cerrar la aplicación:\n\n{args.Exception.Message}",
                "NetPulse Optimizer - Error inesperado",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            args.Handled = true;
        };

        var services = new ServiceCollection();

        var dbPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "NetPulseOptimizer",
            "netpulse.db");

        services.AddSingleton<INetworkAdapterInventoryService, NetworkAdapterInventoryService>();
        services.AddSingleton<ILatencyProbeService, IcmpLatencyProbeService>();
        services.AddSingleton<ITcpConnectProbeService, TcpConnectProbeService>();
        services.AddSingleton<IDnsResolutionProbeService, DnsResolutionProbeService>();
        services.AddSingleton<IDiagnosticHistoryRepository>(_ => new SqliteDiagnosticHistoryRepository(dbPath));
        services.AddSingleton<ConnectionDiagnosticUseCase>();
        services.AddSingleton(_ => new RecommendationEngine(DefaultRecommendationRules.Create()));
        services.AddSingleton<GenerateRecommendationsUseCase>();

        // Phase 5's privileged action: registered so DashboardViewModel can construct
        // SetAdapterDnsServersAction on demand (it needs per-use adapter id + server list
        // that only exist once the user picks a recommendation, so it cannot itself be a
        // singleton - see DashboardViewModel.ApplyDnsRecommendationAsync).
        services.AddSingleton<IAdapterDnsConfigurationReader, WmiAdapterDnsConfigurationReader>();
        services.AddSingleton<IAdapterAdvancedPropertiesReader, WmiAdapterAdvancedPropertiesReader>();
        services.AddSingleton<IElevatedOperationLauncher, ElevatedHelperOperationLauncher>();
        services.AddSingleton<IPrivilegedOperationAuditLog>(_ => new SqlitePrivilegedOperationAuditLog(dbPath));

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
