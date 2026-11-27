using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace PixFix.App.Common;

/// <summary>
/// Hinweisleiste „… gelöscht – Rückgängig“ unten im Hauptfenster. Bleibt 12 Sekunden stehen; danach liegt das Gelöschte
/// weiterhin im Papierkorb.
/// </summary>
public sealed class UndoService : ObservableObject
{
    private static readonly TimeSpan Visible = TimeSpan.FromSeconds(12);

    private readonly DispatcherTimer timer;
    private Action? undo;
    private string text = string.Empty;

    public UndoService()
    {
        timer = new DispatcherTimer { Interval = Visible };
        timer.Tick += (_, _) => Dismiss();
        UndoCommand = new RelayCommand(Undo, () => undo is not null);
        DismissCommand = new RelayCommand(Dismiss);
    }

    public RelayCommand UndoCommand { get; }

    public RelayCommand DismissCommand { get; }

    public string Text
    {
        get => text;
        private set => SetProperty(ref text, value);
    }

    public bool IsVisible => undo is not null;

    /// <summary>Hinweis mit „Rückgängig“ anbieten; ersetzt einen noch sichtbaren älteren Hinweis.</summary>
    public void Offer(string message, Action undoAction)
    {
        undo = undoAction;
        Text = message;
        Changed();
        timer.Stop();
        timer.Start();
    }

    private void Undo()
    {
        var action = undo;
        Dismiss();
        try
        {
            action?.Invoke();
        }
        catch (Exception ex) when (ex is InvalidOperationException or KeyNotFoundException or Microsoft.Data.Sqlite.SqliteException)
        {
            // Z. B. inzwischen endgültig aus dem Papierkorb gelöscht.
            System.Windows.MessageBox.Show("Rückgängig war nicht mehr möglich.\n\n" + ex.Message, "PixFix", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
        }
    }

    private void Dismiss()
    {
        timer.Stop();
        undo = null;
        Changed();
    }

    private void Changed()
    {
        OnPropertyChanged(nameof(IsVisible));
        UndoCommand.NotifyCanExecuteChanged();
    }
}
