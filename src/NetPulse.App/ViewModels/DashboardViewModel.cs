using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NetPulse.Application.Ports;
using NetPulse.Application.UseCases;
using NetPulse.Domain.Network;
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

    // The full, unfiltered inventory from the last refresh - Adapters (the bindable
    // collection the grid shows) is a filtered view over this, re-applied whenever
    // ShowVirtualAdapters changes, without re-querying the OS.
    private List<AdapterRowViewModel> _allAdapters = new();

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
    private string _statusText = "Listo. Todavía no se ha ejecutado ningún diagnóstico.";

    [ObservableProperty]
    private StatusSeverity _statusSeverity = StatusSeverity.Info;

    [ObservableProperty]
    private bool _isRunning;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ApplyDnsRecommendationCommand))]
    private AdapterRowViewModel? _selectedAdapter;

    [ObservableProperty]
    private bool _canRollbackDns;

    /// <summary>
    /// Off by default: Windows always exposes several virtual "WAN Miniport" adapters (one
    /// per VPN protocol it supports) plus tunnel pseudo-interfaces (6to4/Teredo/ISATAP),
    /// through the exact same API as real NICs - a machine with one real Wi-Fi adapter
    /// commonly shows 8-10 entries if none of this is filtered. None of those virtual
    /// adapters is ever useful for diagnosing a real network problem, so they start hidden;
    /// toggling this shows them without re-reading anything from the OS.
    /// </summary>
    [ObservableProperty]
    private bool _showVirtualAdapters;

    partial void OnSelectedProfileChanged(DiagnosticProfile value)
    {
        var settings = DiagnosticProfileCatalog.Get(value);
        SampleCount = settings.SampleCount;
        ProfileFocusText = settings.Focus;
    }

    partial void OnShowVirtualAdaptersChanged(bool value) => ApplyAdapterFilter();

    private void SetStatus(string text, StatusSeverity severity = StatusSeverity.Info)
    {
        StatusText = text;
        StatusSeverity = severity;
    }

    [RelayCommand]
    private async Task LoadAdaptersAsync()
    {
        // This runs from MainWindow's Loaded event handler (an async lambda, which is
        // effectively async void) as well as from the "Refresh adapters" button - any
        // unhandled exception here would crash the whole app via WPF's unhandled-exception
        // path rather than show an error, so every failure must be caught, not just the
        // ones in RunDiagnosticAsync.
        SetStatus("Leyendo inventario de adaptadores...", StatusSeverity.Progress);

        try
        {
            var adapters = await _adapterInventory.GetAdaptersAsync(CancellationToken.None);
            _allAdapters = adapters.Select(a => new AdapterRowViewModel(a)).ToList();
            ApplyAdapterFilter();

            var virtualCount = _allAdapters.Count(a => a.IsVirtual);
            var hiddenNote = !ShowVirtualAdapters && virtualCount > 0
                ? $" ({virtualCount} adaptador(es) virtual(es) de Windows oculto(s) - activa \"Mostrar virtuales\" para verlos)"
                : string.Empty;
            SetStatus($"Se cargaron {Adapters.Count} adaptador(es){hiddenNote}. Esto es solo lectura - no se cambió nada.", StatusSeverity.Success);
        }
        catch (Exception ex)
        {
            SetStatus($"No se pudo leer el inventario de adaptadores: {ex.Message}", StatusSeverity.Error);
        }
    }

    private void ApplyAdapterFilter()
    {
        var previouslySelectedId = SelectedAdapter?.Id;

        Adapters.Clear();
        foreach (var adapter in _allAdapters.Where(a => ShowVirtualAdapters || !a.IsVirtual))
            Adapters.Add(adapter);

        SelectedAdapter = Adapters.FirstOrDefault(a => a.Id == previouslySelectedId);
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
            SetStatus("Ingresa al menos un objetivo (dirección IP u hostname) antes de ejecutar un diagnóstico.", StatusSeverity.Warning);
            return;
        }

        IsRunning = true;
        RunDiagnosticCommand.NotifyCanExecuteChanged();
        _runCancellation = new CancellationTokenSource();
        SetStatus($"Ejecutando {SampleCount} sondeos contra {targets.Count} objetivo(s)...", StatusSeverity.Progress);
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

            SetStatus(
                $"Diagnóstico completo a las {DateTimeOffset.Now:T}. {results.Count} objetivo(s) evaluados, {Recommendations.Count} recomendación(es).",
                StatusSeverity.Success);
        }
        catch (OperationCanceledException)
        {
            SetStatus("Diagnóstico cancelado.", StatusSeverity.Warning);
        }
        catch (Exception ex)
        {
            SetStatus($"El diagnóstico falló: {ex.Message}", StatusSeverity.Error);
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
            $"Adaptador: {adapter.Name}\n" +
            $"Servidores DNS propuestos: {string.Join(", ", SuggestedPublicDnsServers)}\n\n" +
            $"Riesgo: {SpanishDisplayNames.Risk(descriptor.Risk)}\n{descriptor.Objective}\n\n" +
            "Esto va a lanzar un proceso auxiliar elevado y Windows va a mostrar un " +
            "control de UAC. Tienes que aprobarlo tú mismo para que el cambio surta " +
            "efecto - esta aplicación no puede hacerlo por ti.\n\n" +
            $"Reversión: {descriptor.RollbackMethod}\n\n" +
            "¿Continuar?",
            "Confirmar cambio de servidores DNS",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning) == MessageBoxResult.Yes;

        if (!confirmed)
        {
            SetStatus("Cambio de DNS cancelado - no se hizo nada.", StatusSeverity.Info);
            return;
        }

        SetStatus("Comprobando condiciones previas...", StatusSeverity.Progress);
        if (!await action.CheckAsync(CancellationToken.None))
        {
            SetStatus("No se puede aplicar: no se encontró el adaptador seleccionado, o los servidores propuestos no pasaron la validación.", StatusSeverity.Error);
            return;
        }

        SetStatus("Capturando la configuración DNS actual para poder revertir el cambio...", StatusSeverity.Progress);
        _lastDnsCapturedState = await action.CaptureAsync(CancellationToken.None);
        _lastDnsAction = action;

        SetStatus("Aplicando - aprueba el control de UAC de Windows para continuar...", StatusSeverity.Progress);
        var applyResult = await action.ApplyAsync(CancellationToken.None);
        if (applyResult.Outcome == OptimizationOutcome.Failed)
        {
            SetStatus($"El cambio de DNS falló: {applyResult.Error}", StatusSeverity.Error);
            return;
        }

        SetStatus("Verificando que el cambio surtió efecto...", StatusSeverity.Progress);
        var verifyResult = await action.VerifyAsync(CancellationToken.None);
        var verified = verifyResult.Outcome == OptimizationOutcome.VerifiedImproved;
        SetStatus(
            verified
                ? $"Servidores DNS cambiados y verificados: {verifyResult.Details}. Usa Revertir para restaurar la configuración anterior."
                : $"El cambio de DNS se aplicó pero no se pudo verificar ({verifyResult.Details}). Usa Revertir si esto no es lo que esperabas.",
            verified ? StatusSeverity.Success : StatusSeverity.Warning);

        CanRollbackDns = true;
        RollbackDnsCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanRollbackDns))]
    private async Task RollbackDnsAsync()
    {
        if (_lastDnsAction is null || _lastDnsCapturedState is null)
            return;

        SetStatus("Revirtiendo - aprueba el control de UAC de Windows para continuar...", StatusSeverity.Progress);
        var result = await _lastDnsAction.RollbackAsync(_lastDnsCapturedState, CancellationToken.None);

        SetStatus(
            result.Outcome == OptimizationOutcome.RolledBack
                ? "Reversión exitosa - la configuración DNS se restauró a como estaba antes del cambio."
                : $"La reversión falló: {result.Error}",
            result.Outcome == OptimizationOutcome.RolledBack ? StatusSeverity.Success : StatusSeverity.Error);

        if (result.Outcome == OptimizationOutcome.RolledBack)
        {
            CanRollbackDns = false;
            RollbackDnsCommand.NotifyCanExecuteChanged();
        }
    }
}
