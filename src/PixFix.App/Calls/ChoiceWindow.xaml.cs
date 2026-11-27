using System.Windows;

namespace PixFix.App.Calls;

/// <summary>Eine Auswahl aus einer Liste (Telefonnummer, Vorlage, Fax-Dokument …).</summary>
public sealed record Choice<T>(string Label, T Value);

public partial class ChoiceWindow : Window
{
    private ChoiceWindow()
    {
        InitializeComponent();
    }

    /// <summary>Zeigt die Auswahl; liefert den gewählten Wert oder <c>default</c> bei Abbruch.</summary>
    public static T? Pick<T>(Window? owner, string title, string prompt, IReadOnlyList<Choice<T>> choices, int preselect = 0)
    {
        ArgumentNullException.ThrowIfNull(choices);
        var window = new ChoiceWindow { Title = title, Owner = owner };
        window.Prompt.Text = prompt;
        window.Options.ItemsSource = choices;
        window.Options.SelectedIndex = choices.Count == 0 ? -1 : Math.Clamp(preselect, 0, choices.Count - 1);
        window.Loaded += (_, _) => window.Options.Focus();
        return window.ShowDialog() == true && window.Options.SelectedItem is Choice<T> choice ? choice.Value : default;
    }

    private void OnOk(object sender, RoutedEventArgs e)
    {
        if (Options.SelectedItem is not null)
        {
            DialogResult = true;
        }
    }
}
