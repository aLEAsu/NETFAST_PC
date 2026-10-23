namespace NetPulse.App.ViewModels;

/// <summary>
/// Drives the status bar's color so the user can tell at a glance whether the last thing
/// that happened succeeded, failed, or is still running - plain gray italic text for every
/// state (the original design) made it too easy to miss that something had gone wrong.
/// </summary>
public enum StatusSeverity
{
    Info,
    Progress,
    Success,
    Warning,
    Error,
}
