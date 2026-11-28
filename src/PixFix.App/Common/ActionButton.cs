using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using PixFix.Core.Common;

namespace PixFix.App.Common;

/// <summary>Meaning of an action button; drives its color in the "Farbig" appearance.</summary>
public enum ActionRole
{
    /// <summary>Ordering, toggles, navigation – never colored.</summary>
    Neutral,

    /// <summary>Main action of a toolbar – accent color in both appearances.</summary>
    Primary,

    /// <summary>Create or confirm – green.</summary>
    Create,

    /// <summary>Delete or cancel – red.</summary>
    Danger,

    /// <summary>Changes state with consequences (archive, reminder, hang up) – orange.</summary>
    Warning,

    /// <summary>Regular action (search, copy, open, mail) – blue.</summary>
    Action,
}

/// <summary>
/// Attached properties for buttons using the <c>ActionButton</c>/<c>ActionToggleButton</c> styles:
/// glyph (<c>Icon.*</c> resource), role color and a description shown below the label in the tooltip.
/// The button's <c>Content</c> stays the label including its access key (e.g. "_Anrufen" = Alt+A).
/// </summary>
public static class ActionButton
{
    public static readonly DependencyProperty IconProperty = DependencyProperty.RegisterAttached(
        "Icon", typeof(string), typeof(ActionButton), new FrameworkPropertyMetadata(null));

    /// <summary>Vector logo instead of a font glyph (e.g. AnyDesk, TeamViewer); drawn in the button's foreground color.</summary>
    public static readonly DependencyProperty IconGeometryProperty = DependencyProperty.RegisterAttached(
        "IconGeometry", typeof(Geometry), typeof(ActionButton), new FrameworkPropertyMetadata(null));

    /// <summary><c>true</c> draws <see cref="IconGeometryProperty"/> as outline (stroke) instead of filled.</summary>
    public static readonly DependencyProperty IconStrokedProperty = DependencyProperty.RegisterAttached(
        "IconStroked", typeof(bool), typeof(ActionButton), new FrameworkPropertyMetadata(false));

    public static readonly DependencyProperty RoleProperty = DependencyProperty.RegisterAttached(
        "Role", typeof(ActionRole), typeof(ActionButton), new FrameworkPropertyMetadata(ActionRole.Neutral));

    public static readonly DependencyProperty DescriptionProperty = DependencyProperty.RegisterAttached(
        "Description", typeof(string), typeof(ActionButton), new FrameworkPropertyMetadata(null));

    public static string? GetIcon(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return (string?)element.GetValue(IconProperty);
    }

    public static void SetIcon(DependencyObject element, string? value)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetValue(IconProperty, value);
    }

    public static Geometry? GetIconGeometry(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return (Geometry?)element.GetValue(IconGeometryProperty);
    }

    public static void SetIconGeometry(DependencyObject element, Geometry? value)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetValue(IconGeometryProperty, value);
    }

    public static bool GetIconStroked(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return (bool)element.GetValue(IconStrokedProperty);
    }

    public static void SetIconStroked(DependencyObject element, bool value)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetValue(IconStrokedProperty, value);
    }

    public static ActionRole GetRole(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return (ActionRole)element.GetValue(RoleProperty);
    }

    public static void SetRole(DependencyObject element, ActionRole value)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetValue(RoleProperty, value);
    }

    public static string? GetDescription(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return (string?)element.GetValue(DescriptionProperty);
    }

    public static void SetDescription(DependencyObject element, string? value)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetValue(DescriptionProperty, value);
    }
}

/// <summary>Tooltip of an action button: label with Alt shortcut, then the description (<see cref="ActionLabel.ToolTip"/>).</summary>
public sealed class ActionToolTipConverter : IMultiValueConverter
{
    public object? Convert(object?[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(values);
        var label = values.Length > 0 ? values[0] as string : null;
        var description = values.Length > 1 ? values[1] as string : null;
        var text = ActionLabel.ToolTip(label, description);

        // No tooltip at all instead of an empty box.
        return text.Length == 0 ? null : text;
    }

    public object?[] ConvertBack(object? value, Type[] targetTypes, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
