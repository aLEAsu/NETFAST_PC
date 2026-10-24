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
    private readonly IAdapterAdvancedPropertiesReader _advancedPropertiesReader;
    private readonly IProcessNetworkConnectionsReader _processConnectionsReader;
    private readonly IAdapterThroughputReader _adapterThroughputReader;
    private readonly BufferbloatUseCase _bufferbloatUseCase;
    private readonly IElevatedOperationLauncher _elevatedLauncher;
    private readonly IPrivilegedOperationAuditLog _auditLog;
    private CancellationTokenSource? _runCancellation;
    private CancellationTokenSource? _bufferbloatCancellation;
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
        IAdapterAdvancedPropertiesReader advancedPropertiesReader,
        IProcessNetworkConnectionsReader processConnectionsReader,
        IAdapterThroughputReader adapterThroughputReader,
        BufferbloatUseCase bufferbloatUseCase,
        IElevatedOperationLauncher elevatedLauncher,
        IPrivilegedOperationAuditLog auditLog)
    {
        _adapterInventory = adapterInventory;
        _connectionDiagnostic = connectionDiagnostic;
        _generateRecommendations = generateRecommendations;
        _dnsReader = dnsReader;
        _advancedPropertiesReader = advancedPropertiesReader;
        _processConnectionsReader = processConnectionsReader;
        _adapterThroughputReader = adapterThroughputReader;
        _bufferbloatUseCase = bufferbloatUseCase;
        _elevatedLauncher = elevatedLauncher;
        _auditLog = auditLog;
        _packetCaptureAction = new PacketCounterCaptureAction(elevatedLauncher, auditLog);
    }

    private readonly PacketCounterCaptureAction _packetCaptureAction;

    public ObservableCollection<AdapterRowViewModel> Adapters { get; } = new();
    public ObservableCollection<FindingRowViewModel> Findings { get; } = new();
    public ObservableCollection<RecommendationRowViewModel> Recommendations { get; } = new();
    public ObservableCollection<AdvancedPropertyRowViewModel> AdvancedProperties { get; } = new();
    public ObservableCollection<ProcessConnectionRowViewModel> ProcessConnections { get; } = new();
    public ObservableCollection<AdapterThroughputRowViewModel> AdapterThroughput { get; } = new();

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

    [ObservableProperty]
    private string _advancedPropertiesStatusText = "Selecciona un adaptador para ver sus propiedades avanzadas.";

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

    [ObservableProperty]
    private string _processConnectionsStatusText = "Pulsa \"Actualizar\" para ver qué procesos tienen conexiones de red activas ahora mismo.";

    [ObservableProperty]
    private bool _isLoadingProcessConnections;

    public IReadOnlyList<LoadDirection> BufferbloatDirections { get; } = new[] { LoadDirection.Download, LoadDirection.Upload };
    public IReadOnlyList<int> BufferbloatDurationChoices { get; } = new[] { 5, 10, 15, 20, 30 };
    public IReadOnlyList<int> PacketCaptureDurationChoices { get; } = new[] { 5, 15, 30, 60 };

    [ObservableProperty]
    private string _bufferbloatTargetText = "1.1.1.1";

    [ObservableProperty]
    private LoadDirection _selectedBufferbloatDirection = LoadDirection.Download;

    [ObservableProperty]
    private int _selectedBufferbloatDurationSeconds = 10;

    [ObservableProperty]
    private bool _isRunningBufferbloatTest;

    [ObservableProperty]
    private string _bufferbloatStatusText =
        "Mide la latencia en reposo y luego bajo carga real. Esta prueba contacta speed.cloudflare.com - " +
        "no se ejecuta nunca automáticamente, solo cuando pulsas \"Ejecutar prueba de bufferbloat\".";

    [ObservableProperty]
    private int _selectedPacketCaptureDurationSeconds = 15;

    [ObservableProperty]
    private bool _isCapturingPackets;

    [ObservableProperty]
    private string _packetCaptureStatusText = "Sin capturas todavía. Requiere aprobar un control de UAC de Windows.";

    [ObservableProperty]
    private string _packetCaptureRawOutputText = string.Empty;

    partial void OnSelectedProfileChanged(DiagnosticProfile value)
    {
        var settings = DiagnosticProfileCatalog.Get(value);
        SampleCount = settings.SampleCount;
        ProfileFocusText = settings.Focus;
    }

    partial void OnShowVirtualAdaptersChanged(bool value) => ApplyAdapterFilter();

    partial void OnSelectedAdapterChanged(AdapterRowViewModel? value) => _ = LoadAdvancedPropertiesAsync(value);

    /// <summary>
    /// Module C (brief §6.4): read-only advanced driver/hardware properties (RSS, offloads,
    /// EEE, etc.) for whichever adapter is selected - fired automatically on selection
    /// change rather than behind a button, since reading them is cheap and side-effect-free.
    /// Uses its own status line instead of the shared one so it never clobbers a diagnostic
    /// or DNS-change message the user is still reading.
    /// </summary>
    private async Task LoadAdvancedPropertiesAsync(AdapterRowViewModel? adapter)
    {
        AdvancedProperties.Clear();

        if (adapter is null)
        {
            AdvancedPropertiesStatusText = "Selecciona un adaptador para ver sus propiedades avanzadas.";
            return;
        }

        AdvancedPropertiesStatusText = $"Leyendo propiedades avanzadas de {adapter.Name}...";

        try
        {
            var properties = await _advancedPropertiesReader.GetAdvancedPropertiesAsync(adapter.Id, CancellationToken.None);
            foreach (var property in properties)
                AdvancedProperties.Add(new AdvancedPropertyRowViewModel(property));

            AdvancedPropertiesStatusText = properties.Count > 0
                ? $"{properties.Count} propiedad(es) avanzada(s) de {adapter.Name}. Solo lectura - nada aquí se puede cambiar desde esta aplicación."
                : $"Windows no expone propiedades avanzadas para {adapter.Name} (normal en adaptadores virtuales o algunos controladores).";
        }
        catch (Exception ex)
        {
            AdvancedPropertiesStatusText = $"No se pudieron leer las propiedades avanzadas: {ex.Message}";
        }
    }

    /// <summary>
    /// Module F (brief §6.2): a manual, on-demand snapshot - deliberately not a live
    /// auto-refreshing timer, per the project's "no unbounded background polling" principle
    /// (see 01-architecture.md §2). Shows which processes own which active TCP connections
    /// (a real, direct observation) alongside each interface's current real throughput rate
    /// - the two are shown side by side, never combined into a fabricated "bytes used by
    /// this process" figure, because Windows does not expose that without ETW/elevation.
    /// </summary>
    [RelayCommand]
    private async Task RefreshProcessConnectionsAsync()
    {
        IsLoadingProcessConnections = true;
        ProcessConnectionsStatusText = "Leyendo conexiones activas y velocidad de los adaptadores...";

        try
        {
            var connections = await _processConnectionsReader.GetActiveConnectionsAsync(CancellationToken.None);
            var throughput = await _adapterThroughputReader.GetCurrentThroughputAsync(CancellationToken.None);

            ProcessConnections.Clear();
            foreach (var connection in connections)
                ProcessConnections.Add(new ProcessConnectionRowViewModel(connection));

            AdapterThroughput.Clear();
            foreach (var sample in throughput)
                AdapterThroughput.Add(new AdapterThroughputRowViewModel(sample));

            ProcessConnectionsStatusText =
                $"{connections.Count} conexión(es) TCP activa(s) a las {DateTimeOffset.Now:T}. " +
                "Esto muestra qué proceso tiene cada conexión abierta, no cuántos bytes transmite - " +
                "Windows no expone eso sin herramientas de más bajo nivel (ETW), así que esta aplicación no inventa esa cifra.";
        }
        catch (Exception ex)
        {
            ProcessConnectionsStatusText = $"No se pudieron leer las conexiones de red: {ex.Message}";
        }
        finally
        {
            IsLoadingProcessConnections = false;
        }
    }

    private bool CanRunBufferbloatTest() => !IsRunningBufferbloatTest;

    /// <summary>
    /// Module E (brief §6.1): the one path in this app that deliberately generates real
    /// network load, reachable only from this dedicated button with its own status line -
    /// never bundled into "Ejecutar diagnóstico". The automated session that wrote this
    /// method has never triggered it against the real internet itself, per the project's
    /// constraint that only a human, interactively, may trigger a real run of this kind.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanRunBufferbloatTest))]
    private async Task RunBufferbloatTestAsync()
    {
        var target = BufferbloatTargetText.Trim();
        if (target.Length == 0)
        {
            BufferbloatStatusText = "Ingresa un objetivo para medir la latencia (ej. 1.1.1.1).";
            return;
        }

        IsRunningBufferbloatTest = true;
        RunBufferbloatTestCommand.NotifyCanExecuteChanged();
        _bufferbloatCancellation = new CancellationTokenSource();
        var directionText = SelectedBufferbloatDirection == LoadDirection.Download ? "bajada" : "subida";
        BufferbloatStatusText = $"Midiendo latencia en reposo contra {target}...";

        try
        {
            var options = new BufferbloatOptions(target, SelectedBufferbloatDirection, TimeSpan.FromSeconds(SelectedBufferbloatDurationSeconds));

            BufferbloatStatusText = $"Latencia en reposo medida. Generando carga real de {directionText} durante {SelectedBufferbloatDurationSeconds} s contra speed.cloudflare.com mientras se sigue midiendo la latencia...";

            var sample = await _bufferbloatUseCase.RunAsync(options, _bufferbloatCancellation.Token);

            var addedMs = sample.AddedMedianLatency.TotalMilliseconds;
            var severityText = sample.Severity switch
            {
                BufferbloatSeverity.Minimal => "Mínimo - la latencia casi no cambió bajo carga.",
                BufferbloatSeverity.Moderate => "Moderado - hay un aumento perceptible de latencia bajo carga.",
                BufferbloatSeverity.Significant => "Significativo - la latencia bajo carga sube de forma notoria; esto puede afectar videollamadas o juegos mientras algo más descarga/sube.",
                BufferbloatSeverity.Severe => "Severo - la latencia bajo carga sube mucho; es un síntoma clásico de bufferbloat en el router/módem.",
                _ => "Desconocido",
            };

            BufferbloatStatusText =
                $"Resultado ({directionText}, {SelectedBufferbloatDurationSeconds} s contra {target}): " +
                $"en reposo la mediana fue {sample.AtRest.Median.TotalMilliseconds:F0} ms (P95 {sample.AtRest.P95.TotalMilliseconds:F0} ms); " +
                $"bajo carga la mediana fue {sample.UnderLoad.Median.TotalMilliseconds:F0} ms (P95 {sample.UnderLoad.P95.TotalMilliseconds:F0} ms). " +
                $"Latencia agregada por la carga: {addedMs:F0} ms. Clasificación de esta app (no un estándar de la industria): {severityText} " +
                "Esto mide un momento puntual, no un diagnóstico permanente - repite la prueba si quieres confirmar el patrón.";
        }
        catch (OperationCanceledException)
        {
            BufferbloatStatusText = "Prueba de bufferbloat cancelada.";
        }
        catch (Exception ex)
        {
            BufferbloatStatusText = $"La prueba de bufferbloat falló: {ex.Message}";
        }
        finally
        {
            IsRunningBufferbloatTest = false;
            RunBufferbloatTestCommand.NotifyCanExecuteChanged();
            _bufferbloatCancellation = null;
        }
    }

    [RelayCommand]
    private void CancelBufferbloatTest()
    {
        _bufferbloatCancellation?.Cancel();
    }

    private bool CanRunPacketCapture() => !IsCapturingPackets;

    /// <summary>
    /// Module G (brief §6.3): the one path in this app that requires a second UAC-elevated
    /// capability. Same confirmation-then-UAC pattern as the DNS action, and the same
    /// discipline: the automated session that wrote this has never triggered a real
    /// elevated pktmon run - its first real run must be a deliberate, manual action by a
    /// person, exactly like SetAdapterDnsServersAction's first real DNS write was.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanRunPacketCapture))]
    private async Task RunPacketCaptureAsync()
    {
        var descriptor = _packetCaptureAction.Describe();
        var confirmed = MessageBox.Show(
            $"{descriptor.Title}\n\n" +
            $"Duración: {SelectedPacketCaptureDurationSeconds} segundos\n\n" +
            $"{descriptor.Objective}\n\n" +
            "Esto va a lanzar un proceso auxiliar elevado y Windows va a mostrar un " +
            "control de UAC. Tienes que aprobarlo tú mismo para que la captura se " +
            "ejecute - esta aplicación no puede hacerlo por ti.\n\n" +
            $"Privacidad: {string.Join(" ", descriptor.KnownLimitations)}\n\n" +
            "¿Continuar?",
            "Confirmar captura de contadores de paquetes",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning) == MessageBoxResult.Yes;

        if (!confirmed)
        {
            PacketCaptureStatusText = "Captura cancelada - no se hizo nada.";
            return;
        }

        IsCapturingPackets = true;
        RunPacketCaptureCommand.NotifyCanExecuteChanged();
        PacketCaptureStatusText = $"Capturando durante {SelectedPacketCaptureDurationSeconds} s - aprueba el control de UAC de Windows para continuar...";

        try
        {
            var result = await _packetCaptureAction.CaptureAsync(SelectedPacketCaptureDurationSeconds, CancellationToken.None);

            if (result.Succeeded)
            {
                PacketCaptureRawOutputText = result.RawOutput ?? string.Empty;
                PacketCaptureStatusText = $"Captura completa a las {DateTimeOffset.Now:T}. Salida sin procesar de 'pktmon counters' arriba.";
            }
            else
            {
                PacketCaptureStatusText = $"La captura falló: {result.Error}";
            }
        }
        catch (Exception ex)
        {
            PacketCaptureStatusText = $"La captura falló: {ex.Message}";
        }
        finally
        {
            IsCapturingPackets = false;
            RunPacketCaptureCommand.NotifyCanExecuteChanged();
        }
    }

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

        // The previous wording here ("Servidores DNS cambiados y verificados: <raw WMI
        // dump>") was reported as confusing - it didn't say in plain terms whether the
        // change actually took effect or what "verified" meant. Read the observed servers
        // directly (cheap, read-only) instead of parsing them out of verifyResult.Details,
        // and spell out before/requested/now explicitly so there's nothing left to infer.
        var observedServers = await _dnsReader.GetDnsServersAsync(adapter.Id, CancellationToken.None);
        var observedText = observedServers.Count > 0 ? string.Join(", ", observedServers) : "automático (asignado por el router/DHCP)";
        var requestedText = string.Join(", ", SuggestedPublicDnsServers);

        SetStatus(
            verified
                ? $"Listo: el cambio SÍ tomó efecto. El adaptador \"{adapter.Name}\" ahora usa {observedText} - exactamente lo que se pidió ({requestedText}). Si quieres deshacerlo, usa \"Revertir cambio de DNS\"."
                : $"Atención: el cambio NO tomó efecto. Se pidió usar {requestedText}, pero \"{adapter.Name}\" todavía reporta {observedText}. Puede ser que Windows aún no haya aplicado el cambio o que el adaptador lo haya ignorado - prueba \"Revertir cambio de DNS\" para dejarlo como estaba antes, o ejecuta el diagnóstico de nuevo en un momento para volver a comprobar.",
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
        var restoredText = string.IsNullOrEmpty(_lastDnsCapturedState)
            ? "automático (asignado por el router/DHCP)"
            : _lastDnsCapturedState.Replace(",", ", ");
        var result = await _lastDnsAction.RollbackAsync(_lastDnsCapturedState, CancellationToken.None);

        SetStatus(
            result.Outcome == OptimizationOutcome.RolledBack
                ? $"Listo: se restauró el DNS a como estaba antes del cambio ({restoredText})."
                : $"La reversión falló: {result.Error}. El adaptador puede seguir usando los servidores DNS que se aplicaron - vuelve a intentarlo.",
            result.Outcome == OptimizationOutcome.RolledBack ? StatusSeverity.Success : StatusSeverity.Error);

        if (result.Outcome == OptimizationOutcome.RolledBack)
        {
            CanRollbackDns = false;
            RollbackDnsCommand.NotifyCanExecuteChanged();
        }
    }
}
