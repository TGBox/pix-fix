using System.Windows;
using Forms = System.Windows.Forms;

namespace PixFix.App.Common;

/// <summary>
/// Symbol im Infobereich: PixFix läuft nach dem Schließen weiter (Rückruf-Erinnerungen, TI-Monitor, automatische Sicherung)
/// und zeigt Windows-Benachrichtigungen. Beenden geht über das Menü des Symbols.
/// </summary>
public sealed class TrayService : IDisposable
{
    private readonly Forms.NotifyIcon icon;
    private Action? balloonAction;
    private bool hintShown;

    public TrayService()
    {
        icon = new Forms.NotifyIcon
        {
            Text = "PixFix",
            Icon = LoadIcon(),
            Visible = false,
            ContextMenuStrip = new Forms.ContextMenuStrip(),
        };
        icon.DoubleClick += (_, _) => OpenRequested?.Invoke(this, EventArgs.Empty);
        icon.BalloonTipClicked += (_, _) =>
        {
            var action = balloonAction;
            balloonAction = null;
            (action ?? (() => OpenRequested?.Invoke(this, EventArgs.Empty)))();
        };
    }

    /// <summary>Hauptfenster zeigen.</summary>
    public event EventHandler? OpenRequested;

    /// <summary>„Beenden“ im Menü.</summary>
    public event EventHandler? ExitRequested;

    /// <summary>Menüeintrag ergänzen (Reihenfolge = Aufrufreihenfolge); <c>null</c> = Trennlinie.</summary>
    public void AddMenuItem(string? text, Action? action = null, bool bold = false)
    {
        if (text is null)
        {
            icon.ContextMenuStrip!.Items.Add(new Forms.ToolStripSeparator());
            return;
        }

        var item = new Forms.ToolStripMenuItem(text);
        if (bold)
        {
            item.Font = new System.Drawing.Font(item.Font, System.Drawing.FontStyle.Bold);
        }

        item.Click += (_, _) => action?.Invoke();
        icon.ContextMenuStrip!.Items.Add(item);
    }

    public void Show()
    {
        AddMenuItem(null);
        AddMenuItem("PixFix beenden", () => ExitRequested?.Invoke(this, EventArgs.Empty));
        icon.Visible = true;
    }

    /// <summary>Windows-Benachrichtigung; ein Klick darauf führt <paramref name="onClick"/> aus (sonst: Fenster öffnen).</summary>
    public void Notify(string title, string text, bool warning = false, Action? onClick = null)
    {
        if (!icon.Visible)
        {
            return;
        }

        balloonAction = onClick;
        icon.ShowBalloonTip(8000, title, string.IsNullOrWhiteSpace(text) ? " " : text, warning ? Forms.ToolTipIcon.Warning : Forms.ToolTipIcon.Info);
    }

    /// <summary>Beim ersten Schließen einmal erklären, wo PixFix geblieben ist.</summary>
    public void NotifyMinimizedOnce()
    {
        if (!hintShown)
        {
            hintShown = true;
            Notify("PixFix läuft weiter", "Rückruf-Erinnerungen und TI-Monitor bleiben aktiv. Beenden über das PixFix-Symbol im Infobereich.");
        }
    }

    public void Dispose()
    {
        icon.Visible = false;
        icon.ContextMenuStrip?.Dispose();
        icon.Dispose();
    }

    private static System.Drawing.Icon LoadIcon()
    {
        try
        {
            if (Environment.ProcessPath is { } exe && System.Drawing.Icon.ExtractAssociatedIcon(exe) is { } own)
            {
                return own;
            }
        }
        catch (Exception ex) when (ex is ArgumentException or System.IO.IOException)
        {
            // Fallback unten.
        }

        return System.Drawing.SystemIcons.Application;
    }
}
