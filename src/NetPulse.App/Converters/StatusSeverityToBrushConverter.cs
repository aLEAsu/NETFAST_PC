using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using NetPulse.App.ViewModels;

namespace NetPulse.App.Converters;

public sealed class StatusSeverityToBrushConverter : IValueConverter
{
    private static readonly SolidColorBrush InfoBrush = new(Color.FromRgb(0xB5, 0xB5, 0xBD));
    private static readonly SolidColorBrush ProgressBrush = new(Color.FromRgb(0x6B, 0xB6, 0xFF));
    private static readonly SolidColorBrush SuccessBrush = new(Color.FromRgb(0x5E, 0xD6, 0x8C));
    private static readonly SolidColorBrush WarningBrush = new(Color.FromRgb(0xF2, 0xC1, 0x4E));
    private static readonly SolidColorBrush ErrorBrush = new(Color.FromRgb(0xF2, 0x6B, 0x6B));

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is StatusSeverity severity
            ? severity switch
            {
                StatusSeverity.Progress => ProgressBrush,
                StatusSeverity.Success => SuccessBrush,
                StatusSeverity.Warning => WarningBrush,
                StatusSeverity.Error => ErrorBrush,
                _ => InfoBrush,
            }
            : InfoBrush;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
