using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using PixFix.App.Common;

namespace PixFix.App;

public partial class MainWindow : Window
{
    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;

        // Strg+F und Strg+N gehen an die gerade angezeigte Bereichsansicht.
        InputBindings.Add(new KeyBinding(new CommunityToolkit.Mvvm.Input.RelayCommand(() => CurrentTarget()?.FocusSearch()), Key.F, ModifierKeys.Control));
        InputBindings.Add(new KeyBinding(new CommunityToolkit.Mvvm.Input.RelayCommand(() => CurrentTarget()?.CreateNew()), Key.N, ModifierKeys.Control));
    }

    private IShortcutTarget? CurrentTarget() => Find(SectionHost);

    private static IShortcutTarget? Find(DependencyObject parent)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is IShortcutTarget target)
            {
                return target;
            }

            if (Find(child) is { } nested)
            {
                return nested;
            }
        }

        return null;
    }
}
