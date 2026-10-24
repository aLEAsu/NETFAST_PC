using NetPulse.Domain.Network;

namespace NetPulse.App.ViewModels;

/// <summary>Thin, read-only presentation wrapper - no behavior, just formatting for the grid.</summary>
public sealed class ProcessConnectionRowViewModel
{
    public ProcessConnectionRowViewModel(ProcessNetworkConnection connection)
    {
        ProcessId = connection.ProcessId;
        ProcessName = connection.ProcessName;
        LocalEndpoint = $"{connection.LocalAddress}:{connection.LocalPort}";
        RemoteEndpoint = $"{connection.RemoteAddress}:{connection.RemotePort}";
        State = connection.State;
    }

    public int ProcessId { get; }
    public string ProcessName { get; }
    public string LocalEndpoint { get; }
    public string RemoteEndpoint { get; }
    public string State { get; }
}
