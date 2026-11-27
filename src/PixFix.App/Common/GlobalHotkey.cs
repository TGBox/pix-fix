using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;

namespace PixFix.App.Common;

/// <summary>
/// Systemweites Tastenkürzel (RegisterHotKey) – funktioniert auch, wenn PixFix im Hintergrund oder im Infobereich ist.
/// Ist das Kürzel schon von einem anderen Programm belegt, liefert <see cref="TryRegister"/> <c>null</c>.
/// </summary>
public sealed class GlobalHotkey : IDisposable
{
    private const int WmHotkey = 0x0312;
    private const uint ModAlt = 0x1;
    private const uint ModControl = 0x2;
    private const uint ModShift = 0x4;
    private const uint ModNoRepeat = 0x4000;

    private static int nextId = 0x5046;

    private readonly HwndSource source;
    private readonly int id;
    private readonly Action action;

    private GlobalHotkey(HwndSource source, int id, Action action)
    {
        this.source = source;
        this.id = id;
        this.action = action;
        source.AddHook(Hook);
    }

    public static GlobalHotkey? TryRegister(Window window, ModifierKeys modifiers, Key key, Action action)
    {
        ArgumentNullException.ThrowIfNull(window);
        var handle = new WindowInteropHelper(window).EnsureHandle();
        if (HwndSource.FromHwnd(handle) is not { } source)
        {
            return null;
        }

        var flags = ModNoRepeat
            | (modifiers.HasFlag(ModifierKeys.Alt) ? ModAlt : 0)
            | (modifiers.HasFlag(ModifierKeys.Control) ? ModControl : 0)
            | (modifiers.HasFlag(ModifierKeys.Shift) ? ModShift : 0);
        var id = Interlocked.Increment(ref nextId);
        return RegisterHotKey(handle, id, flags, (uint)KeyInterop.VirtualKeyFromKey(key))
            ? new GlobalHotkey(source, id, action)
            : null;
    }

    public void Dispose()
    {
        source.RemoveHook(Hook);
        _ = UnregisterHotKey(source.Handle, id);
    }

    private IntPtr Hook(IntPtr window, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == WmHotkey && wParam.ToInt32() == id)
        {
            handled = true;
            action();
        }

        return IntPtr.Zero;
    }

#pragma warning disable SYSLIB1054
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint virtualKey);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(IntPtr window, int id);
#pragma warning restore SYSLIB1054
}
