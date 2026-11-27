using System.Globalization;
using System.Windows.Data;
using PixFix.Core.Calls;

namespace PixFix.App.Calls;

/// <summary>Zeigt einen <see cref="CallStatus"/> mit seinem deutschen Namen an.</summary>
public sealed class CallStatusNameConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is CallStatus status ? status.ToDisplayName() : string.Empty;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
