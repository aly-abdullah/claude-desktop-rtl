using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace ClaudeDesktopRtl;

internal static class Native
{
    public const uint WM_CLOSE = 0x0010;
    public const ushort VK_CONTROL = 0x11, VK_MENU = 0x12, VK_RETURN = 0x0D, VK_I = 0x49, VK_P = 0x50;

    private const int GWL_STYLE = -16, GWL_EXSTYLE = -20;
    private const long WS_CHILD = 0x40000000L;
    private const long WS_EX_LAYERED = 0x00080000L, WS_EX_TRANSPARENT = 0x00000020L;
    private const uint LWA_ALPHA = 0x2;
    private const uint INPUT_KEYBOARD = 1, KEYEVENTF_KEYUP = 0x2, KEYEVENTF_UNICODE = 0x4;
    private const int SW_RESTORE = 9;
    private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;

    public delegate IntPtr LowLevelKeyboardProc(int code, IntPtr wParam, IntPtr lParam);
    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)]
    public struct MSG { public IntPtr hwnd; public uint message; public IntPtr wParam; public IntPtr lParam; public uint time; public POINT pt; public uint lPrivate; }
    [StructLayout(LayoutKind.Sequential)] private struct LASTINPUTINFO { public uint cbSize; public uint dwTime; }

    [StructLayout(LayoutKind.Sequential)] private struct INPUT { public uint type; public InputUnion U; }
    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public KEYBDINPUT ki;
        [FieldOffset(0)] public MOUSEINPUT mi;
    }
    [StructLayout(LayoutKind.Sequential)] private struct KEYBDINPUT { public ushort wVk; public ushort wScan; public uint dwFlags; public uint time; public IntPtr dwExtraInfo; }
    [StructLayout(LayoutKind.Sequential)] private struct MOUSEINPUT { public int dx; public int dy; public uint mouseData; public uint dwFlags; public uint time; public IntPtr dwExtraInfo; }

    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc cb, IntPtr lParam);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool IsWindow(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern int GetWindowTextLength(IntPtr hWnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int max);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr hWnd, StringBuilder text, int max);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern bool BringWindowToTop(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hWnd, int cmd);
    [DllImport("user32.dll")] private static extern bool AttachThreadInput(uint attach, uint attachTo, bool on);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int index, IntPtr value);
    [DllImport("user32.dll")] private static extern bool SetLayeredWindowAttributes(IntPtr hWnd, uint key, byte alpha, uint flags);
    [DllImport("user32.dll")] private static extern bool GetLayeredWindowAttributes(IntPtr hWnd, out uint key, out byte alpha, out uint flags);
    [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll", SetLastError = true)] private static extern uint SendInput(uint count, INPUT[] inputs, int size);
    [DllImport("user32.dll")] private static extern bool GetLastInputInfo(ref LASTINPUTINFO info);

    [DllImport("user32.dll", SetLastError = true)] public static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc fn, IntPtr hMod, uint threadId);
    [DllImport("user32.dll")] public static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll")] public static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] public static extern int GetMessage(out MSG msg, IntPtr hWnd, uint min, uint max);
    [DllImport("user32.dll")] public static extern bool PostThreadMessage(uint threadId, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll")] public static extern uint GetCurrentThreadId();
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr GetModuleHandle(string name);
    [DllImport("kernel32.dll")] private static extern IntPtr OpenProcess(uint access, bool inherit, int pid);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr h);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern bool QueryFullProcessImageName(IntPtr h, uint flags, StringBuilder name, ref uint size);
    [DllImport("winmm.dll")] public static extern uint timeBeginPeriod(uint ms);

    // ── Processes ────────────────────────────────────────────────────────────────
    public static string ImagePath(int pid)
    {
        IntPtr h = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (h == IntPtr.Zero) return null;
        try
        {
            var sb = new StringBuilder(1024);
            uint size = (uint)sb.Capacity;
            return QueryFullProcessImageName(h, 0, sb, ref size) ? sb.ToString() : null;
        }
        finally { CloseHandle(h); }
    }

    // "claude.exe" is also the Claude Code CLI; the desktop app lives in its Store
    // package (WindowsApps) or, for the classic installer, under AnthropicClaude.
    public static HashSet<uint> DesktopPids()
    {
        var set = new HashSet<uint>();
        foreach (Process p in Process.GetProcessesByName("claude"))
        {
            using (p)
            {
                string path = ImagePath(p.Id);
                if (path != null &&
                    (path.Contains(@"\WindowsApps\", StringComparison.OrdinalIgnoreCase) ||
                     path.Contains(@"\AnthropicClaude\", StringComparison.OrdinalIgnoreCase)))
                    set.Add((uint)p.Id);
            }
        }
        return set;
    }

    [DllImport("ntdll.dll")] private static extern int NtQueryInformationProcess(IntPtr h, int infoClass, IntPtr buffer, int length, out int returned);
    private const int ProcessCommandLineInformation = 60;

    public static string CommandLine(int pid)
    {
        IntPtr h = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (h == IntPtr.Zero) return null;
        IntPtr buffer = IntPtr.Zero;
        try
        {
            NtQueryInformationProcess(h, ProcessCommandLineInformation, IntPtr.Zero, 0, out int needed);
            if (needed <= 0) return null;
            buffer = Marshal.AllocHGlobal(needed);
            if (NtQueryInformationProcess(h, ProcessCommandLineInformation, buffer, needed, out _) != 0) return null;
            int bytes = (ushort)Marshal.ReadInt16(buffer);
            IntPtr text = Marshal.ReadIntPtr(buffer, IntPtr.Size);
            return text == IntPtr.Zero ? null : Marshal.PtrToStringUni(text, bytes / 2);
        }
        catch { return null; }
        finally
        {
            if (buffer != IntPtr.Zero) Marshal.FreeHGlobal(buffer);
            CloseHandle(h);
        }
    }

    private static readonly System.Text.RegularExpressions.Regex UserDataArg =
        new(@"--user-data-dir=(?:""([^""]+)""|(\S+))", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    public static string RunningClaudeDataDir()
    {
        foreach (uint pid in DesktopPids())
        {
            string cmd = CommandLine((int)pid);
            if (cmd == null) continue;
            var m = UserDataArg.Match(cmd);
            if (!m.Success) continue;
            string dir = (m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value).Trim().TrimEnd('\\');
            if (Directory.Exists(dir)) return dir;
        }
        return null;
    }

    public static IEnumerable<Process> ProcessesAt(string exePath, string processName)
    {
        foreach (Process p in Process.GetProcessesByName(processName))
        {
            if (p.Id != Environment.ProcessId && Paths.Same(ImagePath(p.Id), exePath)) yield return p;
            else p.Dispose();
        }
    }

    // ── Windows ──────────────────────────────────────────────────────────────────
    public static string TitleOf(IntPtr hWnd)
    {
        int len = GetWindowTextLength(hWnd);
        if (len <= 0) return string.Empty;
        var sb = new StringBuilder(len + 1);
        GetWindowText(hWnd, sb, sb.Capacity);
        return sb.ToString();
    }

    public static string ClassOf(IntPtr hWnd)
    {
        var sb = new StringBuilder(128);
        GetClassName(hWnd, sb, sb.Capacity);
        return sb.ToString();
    }

    public static bool IsDevToolsTitle(string title) =>
        title.StartsWith("Developer Tools", StringComparison.OrdinalIgnoreCase) ||
        title.StartsWith("DevTools", StringComparison.OrdinalIgnoreCase);

    public static List<IntPtr> WindowsOf(HashSet<uint> pids, bool includeHidden)
    {
        var list = new List<IntPtr>();
        if (pids.Count == 0) return list;
        EnumWindows((h, _) =>
        {
            GetWindowThreadProcessId(h, out uint pid);
            if (pids.Contains(pid) && (includeHidden || IsWindowVisible(h))) list.Add(h);
            return true;
        }, IntPtr.Zero);
        return list;
    }

    // The main window is the largest visible top-level Chromium window of the desktop app.
    public static IntPtr ClaudeMainWindow()
    {
        IntPtr best = IntPtr.Zero;
        long bestArea = 0;
        foreach (IntPtr h in WindowsOf(DesktopPids(), includeHidden: false))
        {
            if (ClassOf(h) != "Chrome_WidgetWin_1") continue;
            if ((GetWindowLongPtr(h, GWL_STYLE).ToInt64() & WS_CHILD) != 0) continue;
            string title = TitleOf(h);
            if (title.Length == 0 || IsDevToolsTitle(title)) continue;
            if (!GetWindowRect(h, out RECT r)) continue;
            long area = (long)(r.Right - r.Left) * (r.Bottom - r.Top);
            if (area > bestArea) { bestArea = area; best = h; }
        }
        return best;
    }

    public static IntPtr DevToolsWindow()
    {
        foreach (IntPtr h in WindowsOf(DesktopPids(), includeHidden: false))
            if (IsDevToolsTitle(TitleOf(h))) return h;
        return IntPtr.Zero;
    }

    public static bool IsVisible(IntPtr hWnd) => IsWindowVisible(hWnd);

    // ── Invisibility: fully transparent and click-through, but still focusable ──
    public static long ExStyle(IntPtr hWnd) => GetWindowLongPtr(hWnd, GWL_EXSTYLE).ToInt64();

    public static void Cloak(IntPtr hWnd)
    {
        SetWindowLongPtr(hWnd, GWL_EXSTYLE, new IntPtr(ExStyle(hWnd) | WS_EX_LAYERED | WS_EX_TRANSPARENT));
        SetLayeredWindowAttributes(hWnd, 0, 0, LWA_ALPHA);
    }

    public static bool IsCloaked(IntPtr hWnd)
    {
        if ((ExStyle(hWnd) & WS_EX_LAYERED) == 0) return false;
        return GetLayeredWindowAttributes(hWnd, out _, out byte alpha, out uint flags) && (flags & LWA_ALPHA) != 0 && alpha == 0;
    }

    public static void Uncloak(IntPtr hWnd, long originalExStyle)
    {
        SetLayeredWindowAttributes(hWnd, 0, 255, LWA_ALPHA);
        SetWindowLongPtr(hWnd, GWL_EXSTYLE, new IntPtr(originalExStyle));
    }

    // ── Focus & input ────────────────────────────────────────────────────────────
    public static bool IsForeground(IntPtr hWnd) => hWnd != IntPtr.Zero && GetForegroundWindow() == hWnd;

    public static void ForceForeground(IntPtr hWnd)
    {
        if (hWnd == IntPtr.Zero) return;
        if (IsIconic(hWnd)) ShowWindow(hWnd, SW_RESTORE);
        uint fg = GetWindowThreadProcessId(GetForegroundWindow(), out _);
        uint me = GetCurrentThreadId();
        bool attached = fg != 0 && fg != me && AttachThreadInput(me, fg, true);
        try { BringWindowToTop(hWnd); SetForegroundWindow(hWnd); }
        finally { if (attached) AttachThreadInput(me, fg, false); }
    }

    public static uint IdleMilliseconds()
    {
        var info = new LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf<LASTINPUTINFO>() };
        return GetLastInputInfo(ref info) ? unchecked((uint)Environment.TickCount - info.dwTime) : uint.MaxValue;
    }

    private static INPUT Key(ushort vk, bool up) => new()
    {
        type = INPUT_KEYBOARD,
        U = new InputUnion { ki = new KEYBDINPUT { wVk = vk, dwFlags = up ? KEYEVENTF_KEYUP : 0 } }
    };

    public static void SendKey(ushort vk) => SendInput(2, new[] { Key(vk, false), Key(vk, true) }, Marshal.SizeOf<INPUT>());

    public static void SendChord(ushort mod1, ushort mod2, ushort key)
    {
        var seq = new List<INPUT>(6);
        if (mod1 != 0) seq.Add(Key(mod1, false));
        if (mod2 != 0) seq.Add(Key(mod2, false));
        seq.Add(Key(key, false));
        seq.Add(Key(key, true));
        if (mod2 != 0) seq.Add(Key(mod2, true));
        if (mod1 != 0) seq.Add(Key(mod1, true));
        SendInput((uint)seq.Count, seq.ToArray(), Marshal.SizeOf<INPUT>());
    }

    public static void TypeText(string text)
    {
        var seq = new List<INPUT>(text.Length * 2);
        foreach (char c in text)
        {
            seq.Add(new INPUT { type = INPUT_KEYBOARD, U = new InputUnion { ki = new KEYBDINPUT { wScan = c, dwFlags = KEYEVENTF_UNICODE } } });
            seq.Add(new INPUT { type = INPUT_KEYBOARD, U = new InputUnion { ki = new KEYBDINPUT { wScan = c, dwFlags = KEYEVENTF_UNICODE | KEYEVENTF_KEYUP } } });
        }
        if (seq.Count > 0) SendInput((uint)seq.Count, seq.ToArray(), Marshal.SizeOf<INPUT>());
    }
}

// Notices real (non-injected) key presses while an apply is in progress, so a user who
// starts typing never has their keystrokes mixed into ours.
internal sealed class KeyboardWatch : IDisposable
{
    private const int WH_KEYBOARD_LL = 13;
    private const int LLKHF_INJECTED = 0x10;
    private const uint WM_QUIT = 0x0012;

    private readonly Thread _thread;
    private readonly ManualResetEventSlim _ready = new(false);
    private Native.LowLevelKeyboardProc _proc;
    private IntPtr _hook;
    private uint _threadId;
    private volatile bool _userTyped;

    public bool UserTyped => _userTyped;

    public KeyboardWatch()
    {
        _thread = new Thread(Loop) { IsBackground = true, Name = "keyboard-watch" };
        _thread.Start();
        _ready.Wait(1000);
    }

    private void Loop()
    {
        _threadId = Native.GetCurrentThreadId();
        _proc = Hook;
        _hook = Native.SetWindowsHookEx(WH_KEYBOARD_LL, _proc, Native.GetModuleHandle(null), 0);
        _ready.Set();
        while (Native.GetMessage(out _, IntPtr.Zero, 0, 0) > 0) { }
        if (_hook != IntPtr.Zero) Native.UnhookWindowsHookEx(_hook);
    }

    private IntPtr Hook(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code >= 0)
        {
            int msg = wParam.ToInt32();
            int flags = Marshal.ReadInt32(lParam, 8);
            if ((msg == 0x0100 || msg == 0x0104) && (flags & LLKHF_INJECTED) == 0) _userTyped = true;
        }
        return Native.CallNextHookEx(IntPtr.Zero, code, wParam, lParam);
    }

    public void Dispose()
    {
        if (_threadId != 0) Native.PostThreadMessage(_threadId, WM_QUIT, IntPtr.Zero, IntPtr.Zero);
        _thread.Join(1000);
        _ready.Dispose();
    }
}
