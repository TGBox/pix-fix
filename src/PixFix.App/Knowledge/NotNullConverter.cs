using System.Globalization;
using System.Windows.Data;

namespace PixFix.App.Knowledge;

/// <summary>True, wenn der Wert nicht null ist (z. B. „ein Eintrag ist ausgewählt“).</summary>
public sealed class NotNullConverter : IValueConverter
{
    public static readonly NotNullConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value is not null;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
