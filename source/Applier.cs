using System.Diagnostics;

namespace ClaudeDesktopRtl;

internal enum ApplyResult { Applied, NotForeground, DevToolsDidNotOpen, Interrupted }

// Runs the saved DevTools snippet inside Claude, invisibly: DevTools is opened with
// Claude's own Ctrl+Alt+I, made fully transparent and click-through the instant it is
// created, used, and closed again. Every keystroke is sent only while that DevTools
// window is confirmed in front and the user has not touched the keyboard, so nothing
// can ever land in Claude's message box.
internal static class Applier
{
    internal static bool Visible;

    public static ApplyResult Run(IntPtr main, int attempt, string snippetName)
    {
        double scale = 1 + 0.5 * Math.Max(0, attempt - 1);
        int Wait(int ms) => (int)(ms * scale);

        HashSet<uint> pids = Native.DesktopPids();
        var before = new HashSet<IntPtr>(Native.WindowsOf(pids, includeHidden: true));
        var cloaked = new Dictionary<IntPtr, long>();
        IntPtr dev = IntPtr.Zero;

        using var keyboard = new KeyboardWatch();
        using var enforcer = new CloakEnforcer(cloaked);

        if (!Native.IsForeground(main)) return ApplyResult.NotForeground;

        try
        {
            Native.SendChord(Native.VK_CONTROL, Native.VK_MENU, Native.VK_I);

            var clock = Stopwatch.StartNew();
            while (dev == IntPtr.Zero && clock.ElapsedMilliseconds < Wait(8000))
            {
                foreach (IntPtr h in Native.WindowsOf(pids, includeHidden: true))
                {
                    if (before.Contains(h) || cloaked.ContainsKey(h)) continue;
                    if (Native.ClassOf(h) != "Chrome_WidgetWin_1") { before.Add(h); continue; }
                    lock (cloaked) cloaked[h] = Native.ExStyle(h);
                    if (!Visible) Native.Cloak(h);
                }
                List<IntPtr> fresh;
                lock (cloaked) fresh = cloaked.Keys.ToList();
                foreach (IntPtr h in fresh)
                    if (Native.IsDevToolsTitle(Native.TitleOf(h))) { dev = h; break; }
                if (dev == IntPtr.Zero) Thread.Sleep(1);
            }

            lock (cloaked)
            {
                foreach (IntPtr h in cloaked.Keys.Where(h => h != dev).ToList())
                {
                    if (Native.IsWindow(h)) Native.Uncloak(h, cloaked[h]);
                    cloaked.Remove(h);
                }
            }
            if (dev == IntPtr.Zero) return ApplyResult.DevToolsDidNotOpen;

            clock.Restart();
            while (!Native.IsVisible(dev) && clock.ElapsedMilliseconds < 3000) Thread.Sleep(5);
            Thread.Sleep(Wait(700));

            bool Ready() => Native.IsForeground(dev) && !keyboard.UserTyped;

            if (!Native.IsForeground(dev)) { Native.ForceForeground(dev); Thread.Sleep(Wait(250)); }

            if (!Ready()) return ApplyResult.Interrupted;
            Native.SendChord(Native.VK_CONTROL, 0, Native.VK_P);
            Thread.Sleep(Wait(450));

            if (!Ready()) return ApplyResult.Interrupted;
            Native.TypeText(snippetName);
            Thread.Sleep(Wait(350));

            if (!Ready()) return ApplyResult.Interrupted;
            Native.SendKey(Native.VK_RETURN);
            Thread.Sleep(Wait(700));

            if (!Ready()) return ApplyResult.Interrupted;
            Native.SendChord(Native.VK_CONTROL, 0, Native.VK_RETURN);
            Thread.Sleep(Wait(600));

            if (Visible) Thread.Sleep(5000);
            return keyboard.UserTyped ? ApplyResult.Interrupted : ApplyResult.Applied;
        }
        finally
        {
            if (dev != IntPtr.Zero)
            {
                Native.PostMessage(dev, Native.WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
                var clock = Stopwatch.StartNew();
                while (Native.IsWindow(dev) && clock.ElapsedMilliseconds < 3000) Thread.Sleep(20);
                if (Native.IsWindow(dev))
                {
                    enforcer.Stop();
                    long original;
                    lock (cloaked) original = cloaked.TryGetValue(dev, out long v) ? v : Native.ExStyle(dev);
                    Native.Uncloak(dev, original);
                    Log.Write("DevTools did not close; made it visible again");
                }
            }
            if (Native.IsWindow(main) && !keyboard.UserTyped) Native.ForceForeground(main);
        }
    }

    // Chromium may rewrite a window's extended style while it is being shown; keep the
    // new windows invisible for the whole apply regardless.
    private sealed class CloakEnforcer : IDisposable
    {
        private readonly Dictionary<IntPtr, long> _windows;
        private readonly Thread _thread;
        private volatile bool _stop;

        public CloakEnforcer(Dictionary<IntPtr, long> windows)
        {
            _windows = windows;
            _thread = new Thread(Loop) { IsBackground = true, Name = "cloak-enforcer" };
            _thread.Start();
        }

        private void Loop()
        {
            while (!_stop)
            {
                lock (_windows)
                {
                    foreach (IntPtr h in _windows.Keys)
                        if (!Visible && Native.IsWindow(h) && !Native.IsCloaked(h)) Native.Cloak(h);
                }
                Thread.Sleep(2);
            }
        }

        public void Stop()
        {
            _stop = true;
            _thread.Join(500);
        }

        public void Dispose() => Stop();
    }
}
