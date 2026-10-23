using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NetPulse.Application.Ports;
using NetPulse.Application.UseCases;

namespace NetPulse.App.ViewModels;

public sealed partial class DashboardViewModel : ObservableObject
{
    private readonly INetworkAdapterInventoryService _adapterInventory;
    private readonly ConnectionDiagnosticUseCase _connectionDiagnostic;
    private CancellationTokenSource? _runCancellation;

    public DashboardViewModel(INetworkAdapterInventoryService adapterInventory, ConnectionDiagnosticUseCase connectionDiagnostic)
    {
        _adapterInventory = adapterInventory;
        _connectionDiagnostic = connectionDiagnostic;
    }

    public ObservableCollection<AdapterRowViewModel> Adapters { get; } = new();
    public ObservableCollection<FindingRowViewModel> Findings { get; } = new();

    [ObservableProperty]
    private string _targetsText = "1.1.1.1, 8.8.8.8";

    [ObservableProperty]
    private int _sampleCount = 20;

    [ObservableProperty]
    private string _statusText = "Ready. No diagnostic has been run yet.";

    [ObservableProperty]
    private bool _isRunning;

    [RelayCommand]
    private async Task LoadAdaptersAsync()
    {
        StatusText = "Reading adapter inventory...";
        Adapters.Clear();

        var adapters = await _adapterInventory.GetAdaptersAsync(CancellationToken.None);
        foreach (var adapter in adapters)
            Adapters.Add(new AdapterRowViewModel(adapter));

        StatusText = $"Loaded {Adapters.Count} adapter(s). This is read-only inventory - nothing was changed.";
    }

    private bool CanRunDiagnostic() => !IsRunning;

    [RelayCommand(CanExecute = nameof(CanRunDiagnostic))]
    private async Task RunDiagnosticAsync()
    {
        var targets = TargetsText
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .ToList();

        if (targets.Count == 0)
        {
            StatusText = "Enter at least one target (IP address or hostname) before running a diagnostic.";
            return;
        }

        IsRunning = true;
        RunDiagnosticCommand.NotifyCanExecuteChanged();
        _runCancellation = new CancellationTokenSource();
        StatusText = $"Running {SampleCount} probes against {targets.Count} target(s)...";
        Findings.Clear();

        try
        {
            var options = new DiagnosticOptions(targets, SampleCount: Math.Max(1, SampleCount));
            var results = await _connectionDiagnostic.RunAsync(options, _runCancellation.Token);

            foreach (var result in results)
                Findings.Add(new FindingRowViewModel(result));

            StatusText = $"Diagnostic complete at {DateTimeOffset.Now:T}. {results.Count} target(s) evaluated.";
        }
        catch (OperationCanceledException)
        {
            StatusText = "Diagnostic cancelled.";
        }
        catch (Exception ex)
        {
            StatusText = $"Diagnostic failed: {ex.Message}";
        }
        finally
        {
            IsRunning = false;
            RunDiagnosticCommand.NotifyCanExecuteChanged();
            _runCancellation = null;
        }
    }

    [RelayCommand]
    private void CancelDiagnostic()
    {
        _runCancellation?.Cancel();
    }
}
