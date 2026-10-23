using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NetPulse.Application.Ports;
using NetPulse.Application.UseCases;
using NetPulse.Domain.Optimization;
using NetPulse.Domain.Profiles;
using NetPulse.PrivilegedAgent;

namespace NetPulse.App.ViewModels;

public sealed partial class DashboardViewModel : ObservableObject
{
    // The DNS servers offered when the user applies the one actionable recommendation this
    // build knows about (see RecommendationRowViewModel.ApplyableDnsRecommendationId) -
    // well-known public resolvers (Cloudflare, Google), shown to the user before anything
    // happens, never applied silently.
    private static readonly string[] SuggestedPublicDnsServers = { "1.1.1.1", "8.8.8.8" };

    private readonly INetworkAdapterInventoryService _adapterInventory;
    private readonly ConnectionDiagnosticUseCase _connectionDiagnostic;
    private readonly GenerateRecommendationsUseCase _generateRecommendations;
    private readonly IAdapterDnsConfigurationReader _dnsReader;
    private readonly IElevatedOperationLauncher _elevatedLauncher;
    private readonly IPrivilegedOperationAuditLog _auditLog;
    private CancellationTokenSource? _runCancellation;
    private SetAdapterDnsServersAction? _lastDnsAction;
    private string? _lastDnsCapturedState;

    public DashboardViewModel(
        INetworkAdapterInventoryService adapterInventory,
        ConnectionDiagnosticUseCase connectionDiagnostic,
        GenerateRecommendationsUseCase generateRecommendations,
        IAdapterDnsConfigurationReader dnsReader,
        IElevatedOperationLauncher elevatedLauncher,
        IPrivilegedOperationAuditLog auditLog)
    {
        _adapterInventory = adapterInventory;
        _connectionDiagnostic = connectionDiagnostic;
        _generateRecommendations = generateRecommendations;
        _dnsReader = dnsReader;
        _elevatedLauncher = elevatedLauncher;
        _auditLog = auditLog;
    }

    public ObservableCollection<AdapterRowViewModel> Adapters { get; } = new();
    public ObservableCollection<FindingRowViewModel> Findings { get; } = new();
    public ObservableCollection<RecommendationRowViewModel> Recommendations { get; } = new();

    public IReadOnlyList<DiagnosticProfile> AvailableProfiles => DiagnosticProfileCatalog.All;

    [ObservableProperty]
    private DiagnosticProfile _selectedProfile = DiagnosticProfile.General;

    [ObservableProperty]
    private string _profileFocusText = DiagnosticProfileCatalog.Get(DiagnosticProfile.General).Focus;

    [ObservableProperty]
    private string _targetsText = "1.1.1.1, 8.8.8.8";

    [ObservableProperty]
    private int _sampleCount = 20;

    [ObservableProperty]
    private string _statusText = "Ready. No diagnostic has been run yet.";

    [ObservableProperty]
    private bool _isRunning;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ApplyDnsRecommendationCommand))]
    private AdapterRowViewModel? _selectedAdapter;

    [ObservableProperty]
    private bool _canRollbackDns;

    partial void OnSelectedProfileChanged(DiagnosticProfile value)
    {
        var settings = DiagnosticProfileCatalog.Get(value);
        SampleCount = settings.SampleCount;
        ProfileFocusText = settings.Focus;
    }

    [RelayCommand]
    private async Task LoadAdaptersAsync()
    {
        // This runs from MainWindow's Loaded event handler (an async lambda, which is
        // effectively async void) as well as from the "Refresh adapters" button - any
        // unhandled exception here would crash the whole app via WPF's unhandled-exception
        // path rather than show an error, so every failure must be caught, not just the
        // ones in RunDiagnosticAsync.
        StatusText = "Reading adapter inventory...";
        Adapters.Clear();

        try
        {
            var adapters = await _adapterInventory.GetAdaptersAsync(CancellationToken.None);
            foreach (var adapter in adapters)
                Adapters.Add(new AdapterRowViewModel(adapter));

            StatusText = $"Loaded {Adapters.Count} adapter(s). This is read-only inventory - nothing was changed.";
        }
        catch (Exception ex)
        {
            StatusText = $"Could not read adapter inventory: {ex.Message}";
        }
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
        Recommendations.Clear();
        CanRollbackDns = false;
        _lastDnsAction = null;
        _lastDnsCapturedState = null;

        try
        {
            var profileSettings = DiagnosticProfileCatalog.Get(SelectedProfile);
            var options = new DiagnosticOptions(
                targets,
                SampleCount: Math.Max(1, SampleCount),
                IntervalBetweenSamples: profileSettings.IntervalBetweenSamples,
                ProbeTimeout: profileSettings.ProbeTimeout);
            var results = await _connectionDiagnostic.RunAsync(options, _runCancellation.Token);

            foreach (var result in results)
                Findings.Add(new FindingRowViewModel(result));

            var recommendations = profileSettings.Prioritize(_generateRecommendations.Run(results));
            foreach (var recommendation in recommendations)
                Recommendations.Add(new RecommendationRowViewModel(recommendation));

            ApplyDnsRecommendationCommand.NotifyCanExecuteChanged();

            StatusText = $"Diagnostic complete at {DateTimeOffset.Now:T}. {results.Count} target(s) evaluated, {Recommendations.Count} recommendation(s).";
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

    private bool CanApplyDnsRecommendation() =>
        SelectedAdapter is not null && Recommendations.Any(r => r.CanApply);

    /// <summary>
    /// The only path in this app that can change a real system setting. Everything before
    /// the confirmation dialog is read-only; nothing below it runs without the user clicking
    /// "Yes" on that dialog, and the elevated write itself still requires a separate,
    /// mandatory Windows UAC approval that this code cannot skip or pre-answer - see
    /// documentation/01-architecture.md SS10. Calling MessageBox.Show directly from a
    /// ViewModel is a pragmatic MVVM compromise for this one confirmation, not a pattern to
    /// repeat elsewhere in the app.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanApplyDnsRecommendation))]
    private async Task ApplyDnsRecommendationAsync()
    {
        var adapter = SelectedAdapter;
        if (adapter is null)
            return;

        var action = new SetAdapterDnsServersAction(
            adapter.Id, SuggestedPublicDnsServers, _adapterInventory, _dnsReader, _elevatedLauncher, _auditLog);
        var descriptor = action.Describe();

        var confirmed = MessageBox.Show(
            $"{descriptor.Title}\n\n" +
            $"Adapter: {adapter.Name}\n" +
            $"Proposed DNS servers: {string.Join(", ", SuggestedPublicDnsServers)}\n\n" +
            $"Risk: {descriptor.Risk}\n{descriptor.Objective}\n\n" +
            "This will launch a separate elevated helper process and Windows will show a " +
            "UAC prompt. You must approve it yourself for the change to take effect - this " +
            "app cannot do that for you.\n\n" +
            $"Rollback: {descriptor.RollbackMethod}\n\n" +
            "Proceed?",
            "Confirm DNS server change",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning) == MessageBoxResult.Yes;

        if (!confirmed)
        {
            StatusText = "DNS change cancelled - nothing was done.";
            return;
        }

        StatusText = "Checking preconditions...";
        if (!await action.CheckAsync(CancellationToken.None))
        {
            StatusText = "Cannot apply: the selected adapter was not found, or the proposed servers failed validation.";
            return;
        }

        StatusText = "Capturing the current DNS configuration so this can be rolled back...";
        _lastDnsCapturedState = await action.CaptureAsync(CancellationToken.None);
        _lastDnsAction = action;

        StatusText = "Applying - approve the Windows UAC prompt to continue...";
        var applyResult = await action.ApplyAsync(CancellationToken.None);
        if (applyResult.Outcome == OptimizationOutcome.Failed)
        {
            StatusText = $"DNS change failed: {applyResult.Error}";
            return;
        }

        StatusText = "Verifying the change took effect...";
        var verifyResult = await action.VerifyAsync(CancellationToken.None);
        var verified = verifyResult.Outcome == OptimizationOutcome.VerifiedImproved;
        StatusText = verified
            ? $"DNS servers changed and verified: {verifyResult.Details}. Use Rollback to restore the previous configuration."
            : $"DNS change applied but could not be verified ({verifyResult.Details}). Use Rollback if this isn't what you expected.";

        CanRollbackDns = true;
        RollbackDnsCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanRollbackDns))]
    private async Task RollbackDnsAsync()
    {
        if (_lastDnsAction is null || _lastDnsCapturedState is null)
            return;

        StatusText = "Rolling back - approve the Windows UAC prompt to continue...";
        var result = await _lastDnsAction.RollbackAsync(_lastDnsCapturedState, CancellationToken.None);

        StatusText = result.Outcome == OptimizationOutcome.RolledBack
            ? "Rollback succeeded - DNS configuration restored to what it was before the change."
            : $"Rollback failed: {result.Error}";

        if (result.Outcome == OptimizationOutcome.RolledBack)
        {
            CanRollbackDns = false;
            RollbackDnsCommand.NotifyCanExecuteChanged();
        }
    }
}
