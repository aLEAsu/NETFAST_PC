namespace NetPulse.Domain.PrivilegedOperations;

/// <summary>
/// Validates that a string is a well-formed adapter identifier (a GUID, with or without
/// braces - <see cref="System.Net.NetworkInformation.NetworkInterface.Id"/> uses braces,
/// WMI's SettingID typically does not). Never accepts anything else, so an adapter id can
/// never be used to smuggle extra command-line content into the elevated helper.
/// </summary>
public static class AdapterIdValidator
{
    public static bool TryValidate(string? adapterId, out string error)
    {
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(adapterId))
        {
            error = "Adapter id is required.";
            return false;
        }

        var trimmed = adapterId.Trim().Trim('{', '}');
        if (!Guid.TryParse(trimmed, out _))
        {
            error = $"'{adapterId}' is not a valid adapter id (expected a GUID).";
            return false;
        }

        return true;
    }

    /// <summary>Normalizes to the bare, lowercase GUID form used when comparing against WMI's SettingID.</summary>
    public static string Normalize(string adapterId) => adapterId.Trim().Trim('{', '}').ToLowerInvariant();
}
