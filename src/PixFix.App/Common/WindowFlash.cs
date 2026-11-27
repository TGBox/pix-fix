using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace PixFix.App.Common;

/// <summary>Taskleistensymbol blinken lassen (FlashWindowEx), z. B. bei fälligen Erinnerungen.</summary>
public static class WindowFlash
{
    private const uint FlashStop = 0;
    private const uint FlashAll = 3;
    private const uint FlashTimerNoForeground = 12;

    public static void Start(Window window) => Flash(window, FlashAll | FlashTimerNoForeground, uint.MaxValue);

    public static void Stop(Window window) => Flash(window, FlashStop, 0);

    /// <summary>Hauptfenster wiederherstellen und nach vorn holen.</summary>
    public static void BringToFront(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);
        if (window.WindowState == WindowState.Minimized)
        {
            window.WindowState = WindowState.Normal;
        }

        window.Show();
        window.Activate();
        window.Topmost = true;
        window.Topmost = false;
        window.Focus();
        Stop(window);
    }

    private static void Flash(Window window, uint flags, uint count)
    {
        ArgumentNullException.ThrowIfNull(window);
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero)
        {
            return;
        }

        var info = new FlashInfo
        {
            Size = (uint)Marshal.SizeOf<FlashInfo>(),
            Window = handle,
            Flags = flags,
            Count = count,
            Timeout = 0,
        };
        _ = FlashWindowEx(ref info);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FlashInfo
    {
        public uint Size;
        public IntPtr Window;
        public uint Flags;
        public uint Count;
        public uint Timeout;
    }

#pragma warning disable SYSLIB1054
    [DllImport("user32.dll")]
    private static extern bool FlashWindowEx(ref FlashInfo info);
#pragma warning restore SYSLIB1054
}
