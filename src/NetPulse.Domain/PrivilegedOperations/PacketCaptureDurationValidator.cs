namespace NetPulse.Domain.PrivilegedOperations;

/// <summary>
/// Bounds how long an elevated pktmon counters session may run. Checked both by the caller
/// (<c>NetPulse.Infrastructure.Windows</c>) and, independently, inside the elevated host
/// itself - the brief requires the hard cap to live at the privileged boundary, not only in
/// the UI, so a bug or bypass upstream still cannot start an unbounded capture.
/// </summary>
public static class PacketCaptureDurationValidator
{
    public const int MinSeconds = 1;
    public const int MaxSeconds = 60;

    public static bool TryValidate(string? durationText, out int durationSeconds, out string error)
    {
        error = string.Empty;
        durationSeconds = 0;

        if (!int.TryParse(durationText, out durationSeconds))
        {
            error = $"'{durationText}' is not a valid capture duration in seconds.";
            return false;
        }

        if (durationSeconds < MinSeconds || durationSeconds > MaxSeconds)
        {
            error = $"Capture duration must be between {MinSeconds} and {MaxSeconds} seconds, got {durationSeconds}.";
            return false;
        }

        return true;
    }
}
