using NetPulse.Application.Ports;
using NetPulse.Domain.Network;

namespace NetPulse.Application.UseCases;

/// <summary>
/// Measures latency at rest, then latency while <see cref="ILoadGenerationService"/>
/// generates real load, and returns the comparison. Contains no transport-specific code -
/// only talks to the ports, so the comparison logic can be unit-tested with fakes that never
/// touch the network. The hard cap here is this project's main technical safeguard for the
/// "never generate substantial, sustained load without a human explicitly, interactively
/// triggering it" constraint: this use case is reachable only from a dedicated button with
/// its own confirmation, never folded into the regular diagnostic run.
/// </summary>
public sealed class BufferbloatUseCase
{
    public static readonly TimeSpan MaxLoadDuration = TimeSpan.FromSeconds(30);

    private readonly ILatencyProbeService _probeService;
    private readonly ILoadGenerationService _loadGenerationService;

    public BufferbloatUseCase(ILatencyProbeService probeService, ILoadGenerationService loadGenerationService)
    {
        _probeService = probeService;
        _loadGenerationService = loadGenerationService;
    }

    public async Task<BufferbloatSample> RunAsync(BufferbloatOptions options, CancellationToken cancellationToken)
    {
        if (options.LoadDuration <= TimeSpan.Zero || options.LoadDuration > MaxLoadDuration)
            throw new ArgumentException($"Load duration must be between 0 and {MaxLoadDuration.TotalSeconds:F0} seconds.", nameof(options));

        var restSamples = new List<LatencySample>(options.RestSampleCount);
        for (int i = 0; i < options.RestSampleCount; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            restSamples.Add(await _probeService.ProbeOnceAsync(options.Target, options.EffectiveProbeTimeout, cancellationToken));
            if (i < options.RestSampleCount - 1)
                await Task.Delay(options.EffectiveSampleInterval, cancellationToken);
        }
        var restStats = LatencyStatistics.From(options.Target, restSamples);

        var loadedSamples = new List<LatencySample>();
        using var loadDurationCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        var loadTask = _loadGenerationService.GenerateLoadAsync(options.Direction, options.LoadDuration, loadDurationCts.Token);
        var samplingTask = SampleWhileLoadingAsync(options, loadedSamples, loadDurationCts.Token);

        loadDurationCts.CancelAfter(options.LoadDuration);
        try
        {
            await Task.WhenAll(loadTask, samplingTask);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // Expected: loadDurationCts fired after LoadDuration elapsed - that is this run
            // finishing on schedule, not a failure. A genuine caller-requested cancellation
            // (cancellationToken itself) still propagates below.
        }

        cancellationToken.ThrowIfCancellationRequested();

        if (loadedSamples.Count == 0)
            loadedSamples.Add(await _probeService.ProbeOnceAsync(options.Target, options.EffectiveProbeTimeout, cancellationToken));

        var loadedStats = LatencyStatistics.From(options.Target, loadedSamples);
        return new BufferbloatSample(restStats, loadedStats, options.Direction, options.LoadDuration);
    }

    private async Task SampleWhileLoadingAsync(BufferbloatOptions options, List<LatencySample> loadedSamples, CancellationToken loadDurationToken)
    {
        try
        {
            while (!loadDurationToken.IsCancellationRequested)
            {
                var sample = await _probeService.ProbeOnceAsync(options.Target, options.EffectiveProbeTimeout, loadDurationToken);
                loadedSamples.Add(sample);
                await Task.Delay(options.EffectiveSampleInterval, loadDurationToken);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected end of the sampling window - the load's own duration cap.
        }
    }
}
