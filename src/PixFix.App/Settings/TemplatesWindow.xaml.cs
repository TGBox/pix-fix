using System.Windows;

namespace PixFix.App.Settings;

/// <summary>Fenster zum Pflegen der Vorlagen, Cobra-Schnelltexte und Fax-Dokumente.</summary>
public partial class TemplatesWindow : Window
{
    private TemplatesWindow()
    {
        InitializeComponent();
    }

    public static void ShowFor(Window? owner, TemplatesViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        var window = new TemplatesWindow { Owner = owner, DataContext = viewModel };
        viewModel.Owner = window;

        // Offene Eingaben (Delay 0,5 s bzw. Fokusverlust) vor dem Schließen übernehmen.
        window.Closing += (_, _) => window.CommitPendingInput();
        window.ShowDialog();
    }

    private void CommitPendingInput()
    {
        if (System.Windows.Input.Keyboard.FocusedElement is System.Windows.Controls.TextBox box)
        {
            box.GetBindingExpression(System.Windows.Controls.TextBox.TextProperty)?.UpdateSource();
        }
    }
}
