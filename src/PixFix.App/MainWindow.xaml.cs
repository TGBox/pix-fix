using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using PixFix.App.Common;

namespace PixFix.App;

public partial class MainWindow : Window
{
    private const double CollapsedSidebarWidth = 56;
    private const double MinExpandedSidebarWidth = 150;

    private readonly MainViewModel viewModel;
    private double expandedSidebarWidth = 190;

    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        this.viewModel = viewModel;
        DataContext = viewModel;

        MainSplitter.Loaded += (_, _) =>
        {
            if (SidebarColumn.Width.IsAbsolute && SidebarColumn.Width.Value >= MinExpandedSidebarWidth)
            {
                expandedSidebarWidth = SidebarColumn.Width.Value;
            }

            ApplySidebarState(viewModel.IsSidebarCollapsed);
        };
        MainSplitter.DragCompleted += (_, _) =>
        {
            if (!viewModel.IsSidebarCollapsed
                && SidebarColumn.Width.IsAbsolute
                && SidebarColumn.Width.Value >= MinExpandedSidebarWidth)
            {
                expandedSidebarWidth = SidebarColumn.Width.Value;
            }
        };
        viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.IsSidebarCollapsed))
            {
                ApplySidebarState(this.viewModel.IsSidebarCollapsed);
            }
        };

        // Strg+F und Strg+N gehen an die gerade angezeigte Bereichsansicht.
        InputBindings.Add(new KeyBinding(new CommunityToolkit.Mvvm.Input.RelayCommand(() => CurrentTarget()?.FocusSearch()), Key.F, ModifierKeys.Control));
        InputBindings.Add(new KeyBinding(new CommunityToolkit.Mvvm.Input.RelayCommand(() => CurrentTarget()?.CreateNew()), Key.N, ModifierKeys.Control));
    }

    private void ApplySidebarState(bool collapsed)
    {
        if (collapsed)
        {
            if (MainSplitter.Visibility == Visibility.Visible
                && SidebarColumn.Width.IsAbsolute
                && SidebarColumn.Width.Value >= MinExpandedSidebarWidth)
            {
                expandedSidebarWidth = SidebarColumn.Width.Value;
            }

            SidebarColumn.MinWidth = CollapsedSidebarWidth;
            SidebarColumn.Width = new GridLength(CollapsedSidebarWidth);
            MainSplitter.Visibility = Visibility.Collapsed;
        }
        else
        {
            SidebarColumn.MinWidth = MinExpandedSidebarWidth;
            SidebarColumn.Width = new GridLength(Math.Max(expandedSidebarWidth, MinExpandedSidebarWidth));
            MainSplitter.Visibility = Visibility.Visible;
        }
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
