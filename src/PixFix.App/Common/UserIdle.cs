using System.Runtime.InteropServices;

namespace PixFix.App.Common;

/// <summary>Zeit seit der letzten Maus- oder Tastatureingabe in Windows (für die automatische Sicherung).</summary>
public static class UserIdle
{
    public static TimeSpan Current
    {
        get
        {
            var info = new LastInputInfo { Size = (uint)Marshal.SizeOf<LastInputInfo>() };
            if (!GetLastInputInfo(ref info))
            {
                return TimeSpan.Zero;
            }

            // Beide Werte sind Millisekunden seit Systemstart (32 Bit, laufen nach ~49 Tagen gemeinsam über).
            var idle = unchecked((uint)Environment.TickCount - info.Time);
            return TimeSpan.FromMilliseconds(idle);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LastInputInfo
    {
        public uint Size;
        public uint Time;
    }

#pragma warning disable SYSLIB1054
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetLastInputInfo(ref LastInputInfo info);
#pragma warning restore SYSLIB1054
}
