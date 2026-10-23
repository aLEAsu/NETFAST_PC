using NetPulse.Domain.Network;

namespace NetPulse.App.ViewModels;

/// <summary>Thin, read-only presentation wrapper - no behavior, just formatting for the grid.</summary>
public sealed class AdvancedPropertyRowViewModel
{
    public AdvancedPropertyRowViewModel(NetworkAdapterAdvancedProperty property)
    {
        Keyword = property.Keyword;
        DisplayName = property.DisplayName;
        DisplayValue = property.DisplayValue;
        RawValue = property.RawValue;
    }

    public string Keyword { get; }
    public string DisplayName { get; }
    public string DisplayValue { get; }
    public string RawValue { get; }
}
