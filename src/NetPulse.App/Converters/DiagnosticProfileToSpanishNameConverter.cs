using System.Globalization;
using System.Windows.Data;
using NetPulse.Domain.Profiles;

namespace NetPulse.App.Converters;

/// <summary>
/// Display-only Spanish names for the ComboBox - the Domain enum/catalog stays in English
/// internally (DiagnosticProfileCatalog's own DisplayName/Focus text and the tests that
/// check it are untouched), so this is purely a presentation-layer translation, not a
/// change to the profile catalog's contract.
/// </summary>
public sealed class DiagnosticProfileToSpanishNameConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is DiagnosticProfile profile
            ? profile switch
            {
                DiagnosticProfile.General => "General",
                DiagnosticProfile.Gaming => "Juegos / baja latencia",
                DiagnosticProfile.Downloads => "Descargas / rendimiento",
                DiagnosticProfile.VideoCalls => "Videollamadas / estabilidad",
                DiagnosticProfile.Conservative => "Conservador",
                DiagnosticProfile.Maintenance => "Mantenimiento / diagnóstico a fondo",
                _ => profile.ToString(),
            }
            : value?.ToString() ?? string.Empty;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException("This converter is display-only.");
}
