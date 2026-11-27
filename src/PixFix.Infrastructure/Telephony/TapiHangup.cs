using System.Runtime.InteropServices;

namespace PixFix.Infrastructure.Telephony;

public enum HangupResult
{
    /// <summary>Gespräch beendet.</summary>
    Done,

    /// <summary>Windows-Telefonie (TAPI) ließ sich nicht öffnen.</summary>
    TapiUnavailable,

    /// <summary>Kein aktives lokales Gespräch – nichts getan.</summary>
    NoActiveCall,

    /// <summary>Mehrere aktive Gespräche – aus Sicherheitsgründen nichts getan.</summary>
    SeveralActiveCalls,

    /// <summary>Der Treiber gibt keine Berechtigung zum Auflegen.</summary>
    NoPermission,

    /// <summary>Der Treiber bietet für diesen Gesprächszustand kein Auflegen an.</summary>
    DropNotSupported,

    /// <summary>Auflegen wurde vom Treiber abgelehnt.</summary>
    DropFailed,
}

/// <summary>
/// Auflegen über die lokale Windows-Telefonie, 1:1 aus PixFix 69 (<c>PixFix.Telephony.cs</c>, SPEC F7):
/// TAPI wird nur für diesen Klick geöffnet (Überwachen-Recht), genau ein aktives Gespräch wird gesucht, erst dann
/// wird kurz das Besitzrecht geholt und aufgelegt. Danach wird alles sofort wieder freigegeben – Unify Phone bleibt
/// unabhängig bedienbar, es gibt keine dauerhafte Leitungsübernahme. Muss auf dem UI-Thread laufen (verborgenes Fenster).
/// </summary>
public static class TapiHangup
{
    private const uint CallSelectLine = 0x00000001;
    private const uint PrivilegeMonitor = 0x00000002;
    private const uint PrivilegeOwner = 0x00000004;
    private const uint CallFeatureDrop = 0x00000080;
    private const uint StateIdle = 0x00000001;
    private const uint StateDisconnected = 0x00004000;
    private const uint StateUnknown = 0x00008000;
    private const uint InitUseHiddenWindow = 0x00000001;

    // Delegat muss für die Laufzeit des Prozesses erhalten bleiben (wird von TAPI zurückgerufen).
    private static readonly LineCallback NoopCallback = (_, _, _, _, _, _) => { };

    public static async Task<HangupResult> HangUpSingleLocalCallAsync()
    {
        var app = IntPtr.Zero;
        var lines = new List<IntPtr>();
        var calls = new List<IntPtr>();
        try
        {
            uint apiVersion = 0x00020002;
            var init = new InitializeExParams
            {
                TotalSize = (uint)Marshal.SizeOf<InitializeExParams>(),
                Options = InitUseHiddenWindow,
            };
            if (lineInitializeExW(out app, IntPtr.Zero, NoopCallback, "PixFix – Auflegen", out var deviceCount, ref apiVersion, ref init) < 0 || app == IntPtr.Zero)
            {
                return HangupResult.TapiUnavailable;
            }

            for (uint device = 0; device < deviceCount; device++)
            {
                if (lineNegotiateAPIVersion(app, device, 0x00020000, 0x00020002, out var api, out _) < 0)
                {
                    continue;
                }

                if (lineOpen(app, device, out var line, api, 0, IntPtr.Zero, PrivilegeMonitor, 0, IntPtr.Zero) < 0 || line == IntPtr.Zero)
                {
                    continue;
                }

                lines.Add(line);
                foreach (var call in CallsOnLine(line))
                {
                    if (!calls.Contains(call))
                    {
                        calls.Add(call);
                    }
                }
            }

            var active = calls
                .Where(c => TryGetCallStatus(c, out var state, out _, out _) && state != StateIdle && state != StateDisconnected && state != StateUnknown)
                .ToList();
            if (active.Count == 0)
            {
                return HangupResult.NoActiveCall;
            }

            if (active.Count != 1)
            {
                return HangupResult.SeveralActiveCalls;
            }

            var target = active[0];
            if (!TryGetCallStatus(target, out _, out var privilege, out _))
            {
                return HangupResult.NoActiveCall;
            }

            if ((privilege & PrivilegeOwner) == 0 && lineSetCallPrivilege(target, PrivilegeOwner) < 0)
            {
                return HangupResult.NoPermission;
            }

            if (!TryGetCallStatus(target, out var currentState, out _, out var features) || currentState == StateIdle || currentState == StateDisconnected)
            {
                return HangupResult.NoActiveCall;
            }

            if ((features & CallFeatureDrop) == 0)
            {
                return HangupResult.DropNotSupported;
            }

            if (lineDrop(target, IntPtr.Zero, 0) < 0)
            {
                return HangupResult.DropFailed;
            }

            // Bis zu 1,4 s warten, bis der Treiber das Gespräch als beendet meldet (Nachrichten laufen dabei weiter).
            var until = DateTime.UtcNow.AddMilliseconds(1400);
            while (DateTime.UtcNow < until)
            {
                await Task.Delay(60).ConfigureAwait(true);
                if (!TryGetCallStatus(target, out var s, out _, out _) || s == StateIdle || s == StateDisconnected)
                {
                    break;
                }
            }

            return HangupResult.Done;
        }
        finally
        {
            foreach (var call in calls)
            {
                _ = lineDeallocateCall(call);
            }

            foreach (var line in lines)
            {
                _ = lineClose(line);
            }

            if (app != IntPtr.Zero)
            {
                _ = lineShutdown(app);
            }
        }
    }

    private static bool TryGetCallStatus(IntPtr call, out uint state, out uint privilege, out uint features)
    {
        state = privilege = features = 0;
        const int size = 96;
        var memory = Marshal.AllocHGlobal(size);
        try
        {
            for (var i = 0; i < size; i += 4)
            {
                Marshal.WriteInt32(memory, i, 0);
            }

            Marshal.WriteInt32(memory, 0, size);
            if (lineGetCallStatus(call, memory) < 0)
            {
                return false;
            }

            state = unchecked((uint)Marshal.ReadInt32(memory, 12));
            privilege = unchecked((uint)Marshal.ReadInt32(memory, 20));
            features = unchecked((uint)Marshal.ReadInt32(memory, 24));
            return true;
        }
        finally
        {
            Marshal.FreeHGlobal(memory);
        }
    }

    private static List<IntPtr> CallsOnLine(IntPtr line)
    {
        var result = new List<IntPtr>();
        var size = 512;
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var memory = Marshal.AllocHGlobal(size);
            try
            {
                for (var i = 0; i < size; i += 4)
                {
                    Marshal.WriteInt32(memory, i, 0);
                }

                Marshal.WriteInt32(memory, 0, size);
                if (lineGetNewCalls(line, 0, CallSelectLine, memory) < 0)
                {
                    return result;
                }

                var needed = Marshal.ReadInt32(memory, 4);
                if (needed > size && attempt == 0)
                {
                    size = Math.Min(Math.Max(needed, 1024), 65536);
                    continue;
                }

                var count = Math.Max(0, Marshal.ReadInt32(memory, 12));
                var bytes = Math.Max(0, Marshal.ReadInt32(memory, 16));
                var offset = Math.Max(0, Marshal.ReadInt32(memory, 20));
                if (count <= 0 || bytes <= 0 || offset <= 0 || offset >= size)
                {
                    return result;
                }

                var stride = Math.Max(IntPtr.Size, bytes / count);
                for (var i = 0; i < count; i++)
                {
                    var at = offset + (i * stride);
                    if (at + IntPtr.Size > size)
                    {
                        break;
                    }

                    var call = Marshal.ReadIntPtr(memory, at);
                    if (call != IntPtr.Zero && !result.Contains(call))
                    {
                        result.Add(call);
                    }
                }

                return result;
            }
            finally
            {
                Marshal.FreeHGlobal(memory);
            }
        }

        return result;
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate void LineCallback(IntPtr device, uint message, IntPtr instance, IntPtr param1, IntPtr param2, IntPtr param3);

    [StructLayout(LayoutKind.Sequential)]
    private struct InitializeExParams
    {
        public uint TotalSize;
        public uint NeededSize;
        public uint UsedSize;
        public uint Options;
        public IntPtr Handles;
        public uint CompletionKey;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ExtensionId
    {
        public uint Id0;
        public uint Id1;
        public uint Id2;
        public uint Id3;
    }

#pragma warning disable SYSLIB1054 // P/Invoke wie in PixFix 69 erprobt; kein Source-Generator nötig.
    [DllImport("tapi32.dll", CharSet = CharSet.Unicode)]
    private static extern int lineInitializeExW(out IntPtr lineApp, IntPtr instance, LineCallback callback, string appName, out uint deviceCount, ref uint apiVersion, ref InitializeExParams parameters);

    [DllImport("tapi32.dll")]
    private static extern int lineNegotiateAPIVersion(IntPtr lineApp, uint deviceId, uint lowVersion, uint highVersion, out uint apiVersion, out ExtensionId extensionId);

    [DllImport("tapi32.dll")]
    private static extern int lineOpen(IntPtr lineApp, uint deviceId, out IntPtr line, uint apiVersion, uint extVersion, IntPtr callbackInstance, uint privileges, uint mediaModes, IntPtr callParams);

    [DllImport("tapi32.dll")]
    private static extern int lineGetNewCalls(IntPtr line, uint addressId, uint select, IntPtr callList);

    [DllImport("tapi32.dll")]
    private static extern int lineGetCallStatus(IntPtr call, IntPtr callStatus);

    [DllImport("tapi32.dll")]
    private static extern int lineSetCallPrivilege(IntPtr call, uint privilege);

    [DllImport("tapi32.dll")]
    private static extern int lineDrop(IntPtr call, IntPtr userUserInfo, uint size);

    [DllImport("tapi32.dll")]
    private static extern int lineDeallocateCall(IntPtr call);

    [DllImport("tapi32.dll")]
    private static extern int lineClose(IntPtr line);

    [DllImport("tapi32.dll")]
    private static extern int lineShutdown(IntPtr lineApp);
#pragma warning restore SYSLIB1054
}
