namespace NetPulse.Domain.Profiles;

/// <summary>
/// A profile is a set of rules and priorities - which diagnostic parameters to use and
/// which findings to surface first - never a package of changes applied indiscriminately.
/// No profile here ever applies anything by itself; see DiagnosticProfileCatalog's own
/// doc comment.
/// </summary>
public enum DiagnosticProfile
{
    General,
    Gaming,
    Downloads,
    VideoCalls,
    Conservative,
    Maintenance,
}
