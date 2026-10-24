using System.Diagnostics;
using System.Text.Json;
using NetPulse.Application.Ports;
using NetPulse.Domain.PrivilegedOperations;

namespace NetPulse.Infrastructure.Windows.Network;

/// <summary>
/// Launches NetPulse.PrivilegedAgent.Host as a separate process with the "runas" verb, so
/// Windows itself prompts for UAC consent for exactly this one operation - there is no
/// persistent elevated service and this process never runs elevated itself. The child
/// process's command line is built entirely from already-validated, individually-typed
/// values (never a concatenated free-form string), and it is re-validated again inside the
/// host regardless, per the brief's requirement that the privileged boundary never trust a
/// caller.
///
/// IMPORTANT: this class has never been exercised against a real elevated process in this
/// codebase's automated tests or by the agent that wrote it - doing so would trigger a real
/// UAC prompt and a real system change, which is outside what an automated coding session
/// is allowed to perform. It is written to the same standard as the rest of the codebase and
/// compiles, but its first real run must be a deliberate, manual action by the person using
/// the finished application.
/// </summary>
public sealed class ElevatedHelperOperationLauncher : IElevatedOperationLauncher
{
    private const string HostExecutableName = "NetPulse.PrivilegedAgent.Host.exe";

    public async Task<PrivilegedOperationResult> ExecuteAsync(PrivilegedOperationRequest request, CancellationToken cancellationToken)
    {
        var validationError = request.Capability switch
        {
            PrivilegedCapability.SetAdapterDnsServers => ValidateSetAdapterDnsServers(request),
            PrivilegedCapability.CapturePacketCounters => ValidateCapturePacketCounters(request),
            _ => $"Capability '{request.Capability}' has no client-side validation registered.",
        };
        if (validationError is not null)
            return Failed(request, validationError);

        var hostPath = Path.Combine(AppContext.BaseDirectory, HostExecutableName);
        if (!File.Exists(hostPath))
            return Failed(request, $"Elevated helper not found at '{hostPath}'. It must be deployed alongside the main application.");

        var resultDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "NetPulseOptimizer",
            "elevated-results");
        Directory.CreateDirectory(resultDirectory);
        var resultFilePath = Path.Combine(resultDirectory, $"{Guid.NewGuid():N}.json");

        var startInfo = new ProcessStartInfo
        {
            FileName = hostPath,
            UseShellExecute = true,
            Verb = "runas",
            WindowStyle = ProcessWindowStyle.Hidden,
        };
        startInfo.ArgumentList.Add(request.Capability.ToString());
        startInfo.ArgumentList.Add(request.AdapterId);
        startInfo.ArgumentList.Add(string.Join(",", request.Parameters));
        startInfo.ArgumentList.Add(resultFilePath);

        try
        {
            using var process = Process.Start(startInfo);
            if (process is null)
                return Failed(request, "The elevated helper process could not be started.");

            await process.WaitForExitAsync(cancellationToken);

            if (!File.Exists(resultFilePath))
                return Failed(request, $"The elevated helper exited with code {process.ExitCode} but wrote no result.");

            var json = await File.ReadAllTextAsync(resultFilePath, cancellationToken);
            File.Delete(resultFilePath);

            var parsed = JsonSerializer.Deserialize<HostResult>(json)
                ?? throw new InvalidOperationException("The elevated helper's result file could not be parsed.");

            return new PrivilegedOperationResult(request.Capability, parsed.Success, parsed.Output, parsed.Error, DateTimeOffset.UtcNow);
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            // Thrown when the user declines the UAC prompt (ERROR_CANCELLED).
            return Failed(request, $"Elevation was not granted: {ex.Message}");
        }
        catch (OperationCanceledException)
        {
            throw; // Cancellation must propagate, not be reported as an operation failure.
        }
        catch (Exception ex)
        {
            // Covers a malformed/truncated result file (e.g. the elevated process was killed
            // mid-write) or any other unexpected failure talking to it - this boundary must
            // never let an exception escape uncaught, per the brief's requirement that every
            // privileged-operation outcome be reported clearly rather than crash the caller.
            return Failed(request, $"Elevated helper execution failed: {ex.Message}");
        }
    }

    private static string? ValidateSetAdapterDnsServers(PrivilegedOperationRequest request)
    {
        if (!AdapterIdValidator.TryValidate(request.AdapterId, out var adapterError))
            return adapterError;

        return DnsServerAddressValidator.TryValidate(request.Parameters, out var dnsError) ? null : dnsError;
    }

    private static string? ValidateCapturePacketCounters(PrivilegedOperationRequest request)
    {
        if (request.Parameters.Count != 1)
            return $"Expected exactly one parameter (capture duration in seconds), got {request.Parameters.Count}.";

        return PacketCaptureDurationValidator.TryValidate(request.Parameters[0], out _, out var error) ? null : error;
    }

    private static PrivilegedOperationResult Failed(PrivilegedOperationRequest request, string error) =>
        new(request.Capability, Succeeded: false, Output: null, Error: error, DateTimeOffset.UtcNow);

    private sealed record HostResult(bool Success, string? Output, string? Error);
}
