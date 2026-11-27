using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using PixFix.Infrastructure.Ti;

namespace PixFix.App.Ti;

/// <summary>Farben des TI-Monitors wie PixFix 69: grün = keine Störung, rot = Störung, orange = Prüfung unvollständig, grau = nicht geprüft.</summary>
public sealed class TiStateBrushConverter : IValueConverter
{
    private static readonly Brush Ok = Freeze(new SolidColorBrush(Color.FromRgb(22, 146, 79)));
    private static readonly Brush Incident = Freeze(new SolidColorBrush(Color.FromRgb(204, 35, 46)));
    private static readonly Brush Unknown = Freeze(new SolidColorBrush(Color.FromRgb(218, 139, 0)));
    private static readonly Brush NotChecked = Freeze(new SolidColorBrush(Color.FromRgb(120, 130, 145)));

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        TiState.Ok => Ok,
        TiState.Incident => Incident,
        TiState.Unknown => Unknown,
        _ => NotChecked,
    };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();

    private static Brush Freeze(Brush brush)
    {
        brush.Freeze();
        return brush;
    }
}
