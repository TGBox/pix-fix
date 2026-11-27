using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace PixFix.Infrastructure.Remote;

/// <summary>Ergebnis beim Öffnen eines Fernwartungsprogramms.</summary>
public enum RemoteLaunchStatus
{
    /// <summary>Lief schon und wurde in den Vordergrund geholt.</summary>
    Activated,

    /// <summary>Gestartet und Fenster gefunden.</summary>
    Started,

    /// <summary>Gestartet, aber nach 8 s kein Fenster (evtl. im Infobereich).</summary>
    StartedWithoutWindow,

    /// <summary>Keine normale AnyDesk-Verknüpfung gefunden – AnyDesk wird bewusst nicht über die EXE gestartet.</summary>
    ShortcutMissing,

    StartFailed,
}

public sealed record RemoteLaunchResult(RemoteLaunchStatus Status, string? Detail = null, string? ShortcutUsed = null);

/// <summary>
/// AnyDesk und TeamViewer öffnen wie PixFix 69 (SPEC F8): laufendes Fenster in den Vordergrund holen, sonst starten –
/// AnyDesk nur über die normale Windows-Verknüpfung (.lnk), nie direkt über die EXE (UAC/Dienst-Start), immer mit
/// <c>%TEMP%</c> als Arbeitsverzeichnis, damit kein PixFix-Ordner gesperrt wird.
/// </summary>
public static class RemoteSupportLauncher
{
    private static readonly string[] AnyDeskProcesses = ["AnyDesk"];
    private static readonly string[] TeamViewerProcesses = ["TeamViewer", "TeamViewer_Desktop"];

    /// <param name="rememberedShortcut">Zuletzt funktionierende Verknüpfung aus den Einstellungen (oder leer).</param>
    public static async Task<RemoteLaunchResult> OpenAnyDeskAsync(string? rememberedShortcut)
    {
        var existing = FindWindow(AnyDeskProcesses, requireVisible: true);
        if (existing != IntPtr.Zero)
        {
            await ActivateAsync(existing).ConfigureAwait(true);
            return new RemoteLaunchResult(RemoteLaunchStatus.Activated);
        }

        var shortcut = IsValidAnyDeskShortcut(rememberedShortcut)
            ? rememberedShortcut!
            : AnyDeskShortcutCandidates(DefaultShortcutRoots()).FirstOrDefault();
        if (shortcut is null)
        {
            return new RemoteLaunchResult(RemoteLaunchStatus.ShortcutMissing);
        }

        var result = await StartAndWaitAsync(shortcut, AnyDeskProcesses, requireVisible: true).ConfigureAwait(true);
        return result with { ShortcutUsed = shortcut };
    }

    public static async Task<RemoteLaunchResult> OpenTeamViewerAsync()
    {
        var existing = FindWindow(TeamViewerProcesses, requireVisible: false);
        if (existing != IntPtr.Zero)
        {
            await ActivateAsync(existing).ConfigureAwait(true);
            return new RemoteLaunchResult(RemoteLaunchStatus.Activated);
        }

        var executable = RegistryAppPath("TeamViewer.exe")
            ?? TeamViewerCandidates().FirstOrDefault(File.Exists)
            ?? "TeamViewer.exe";
        return await StartAndWaitAsync(executable, TeamViewerProcesses, requireVisible: false).ConfigureAwait(true);
    }

    /// <summary>Gültig ist eine vorhandene .lnk-Datei, deren Name „AnyDesk“ enthält.</summary>
    public static bool IsValidAnyDeskShortcut(string? path) =>
        !string.IsNullOrWhiteSpace(path)
        && string.Equals(Path.GetExtension(path), ".lnk", StringComparison.OrdinalIgnoreCase)
        && Path.GetFileName(path).Contains("AnyDesk", StringComparison.OrdinalIgnoreCase)
        && File.Exists(path);

    /// <summary>Erst die üblichen Orte (Desktop, Startmenü), nur wenn dort nichts liegt rekursiv suchen – wie PixFix 69.</summary>
    public static IReadOnlyList<string> AnyDeskShortcutCandidates(IEnumerable<string> roots)
    {
        var rootList = roots.Where(r => !string.IsNullOrWhiteSpace(r)).ToList();
        var result = rootList
            .SelectMany(root => new[]
            {
                Path.Combine(root, "AnyDesk.lnk"),
                Path.Combine(root, "Programs", "AnyDesk.lnk"),
                Path.Combine(root, "Programs", "AnyDesk", "AnyDesk.lnk"),
            })
            .Where(IsValidAnyDeskShortcut)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (result.Count > 0)
        {
            return result;
        }

        foreach (var root in rootList.Where(Directory.Exists))
        {
            try
            {
                result.AddRange(Directory.EnumerateFiles(root, "AnyDesk*.lnk", SearchOption.AllDirectories).Where(IsValidAnyDeskShortcut));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Nicht lesbare Ordner überspringen.
            }
        }

        return result.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static IEnumerable<string> DefaultShortcutRoots() =>
    [
        Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
        Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory),
        Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),
        Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu),
    ];

    private static IEnumerable<string> TeamViewerCandidates() =>
    [
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "TeamViewer", "TeamViewer.exe"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "TeamViewer", "TeamViewer.exe"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "TeamViewer", "TeamViewer.exe"),
    ];

    private static string? RegistryAppPath(string executable)
    {
        var sub = @"Software\Microsoft\Windows\CurrentVersion\App Paths\" + executable;
        foreach (var hive in new[] { Registry.CurrentUser, Registry.LocalMachine })
        {
            using var key = hive.OpenSubKey(sub);
            if (key?.GetValue(null) is string value && File.Exists(value.Trim('"')))
            {
                return value.Trim('"');
            }
        }

        return null;
    }

    private static async Task<RemoteLaunchResult> StartAndWaitAsync(string file, string[] processNames, bool requireVisible)
    {
        try
        {
            using var started = Process.Start(new ProcessStartInfo
            {
                FileName = file,
                UseShellExecute = true,
                WorkingDirectory = Path.GetTempPath(),
            });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or FileNotFoundException)
        {
            return new RemoteLaunchResult(RemoteLaunchStatus.StartFailed, ex.Message);
        }

        for (var i = 0; i < 80; i++)
        {
            await Task.Delay(100).ConfigureAwait(true);
            var window = FindWindow(processNames, requireVisible);
            if (window != IntPtr.Zero)
            {
                await ActivateAsync(window).ConfigureAwait(true);
                return new RemoteLaunchResult(RemoteLaunchStatus.Started);
            }
        }

        return new RemoteLaunchResult(RemoteLaunchStatus.StartedWithoutWindow);
    }

    // ------------------------------------------------------------------ Fenster (Win32)

    /// <summary>Hauptfenster eines der Prozesse: zuerst MainWindowHandle, sonst ein Fenster mit Titel per EnumWindows.</summary>
    private static IntPtr FindWindow(string[] processNames, bool requireVisible)
    {
        if (!requireVisible)
        {
            foreach (var name in processNames)
            {
                foreach (var process in Process.GetProcessesByName(name))
                {
                    using (process)
                    {
                        if (process.MainWindowHandle != IntPtr.Zero)
                        {
                            return process.MainWindowHandle;
                        }
                    }
                }
            }
        }

        var found = IntPtr.Zero;
        _ = EnumWindows(
            (window, parameter) =>
            {
                _ = GetWindowThreadProcessId(window, out var pid);
                if (pid == 0 || !ProcessMatches(pid, processNames))
                {
                    return true;
                }

                if (requireVisible && !IsWindowVisible(window) && !IsIconic(window))
                {
                    return true;
                }

                if (GetWindowTextLength(window) <= 0 && !IsIconic(window))
                {
                    return true;
                }

                found = window;
                return false;
            },
            IntPtr.Zero);
        return found;
    }

    private static bool ProcessMatches(uint pid, string[] names)
    {
        try
        {
            using var process = Process.GetProcessById((int)pid);
            return names.Any(n => string.Equals(process.ProcessName, n, StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return false;
        }
    }

    private static async Task ActivateAsync(IntPtr window)
    {
        const int Restore = 9;
        const int Show = 5;
        if (IsIconic(window))
        {
            _ = ShowWindow(window, Restore);
            await Task.Delay(120).ConfigureAwait(true);
        }
        else
        {
            _ = ShowWindow(window, Show);
        }

        _ = SetForegroundWindow(window);
        await Task.Delay(80).ConfigureAwait(true);
        _ = SetForegroundWindow(window);
    }

    private delegate bool EnumWindowsProc(IntPtr window, IntPtr parameter);

#pragma warning disable SYSLIB1054
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr parameter);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(IntPtr window);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextLength(IntPtr window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(IntPtr window, int command);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr window);
#pragma warning restore SYSLIB1054
}
