namespace ClaudeDesktopRtl;

// The silent background part. Started with Windows (no window, no tray icon), it waits
// for Claude, applies the fix once per Claude window, and keeps the snippet up to date
// whenever Claude is closed.
internal static class Agent
{
    private const int MaxAttemptsPerWindow = 6;

    private sealed class Tracker
    {
        public IntPtr Window;
        public DateTime FirstSeen;
        public bool Applied;
        public int Attempts;
        public DateTime NextTry;
        public bool Forced;
        public bool CheckedWhileClosed;
        public bool ToldWaitingForRestart;
    }

    public static int Run(bool applyNow)
    {
        using var mutex = new Mutex(true, Names.AgentMutex, out bool first);
        if (!first)
        {
            if (applyNow) SignalApplyNow();
            return 0;
        }

        using var quit = new EventWaitHandle(false, EventResetMode.ManualReset, Names.QuitEvent);
        quit.Reset();
        using var apply = new EventWaitHandle(false, EventResetMode.AutoReset, Names.ApplyNowEvent);
        Native.timeBeginPeriod(1);

        Log.Write("agent started" + (applyNow ? " (apply now)" : ""));
        var t = new Tracker { Forced = applyNow };
        var handles = new WaitHandle[] { quit, apply };

        while (true)
        {
            int signal = WaitHandle.WaitAny(handles, 1000);
            if (signal == 0) break;
            if (signal == 1) t.Forced = true;
            try { Tick(t); }
            catch (Exception ex) { Log.Write("tick failed: " + ex.Message); }
        }

        Log.Write("agent stopped");
        GC.KeepAlive(mutex);
        return 0;
    }

    public static void SignalApplyNow()
    {
        if (EventWaitHandle.TryOpenExisting(Names.ApplyNowEvent, out EventWaitHandle e))
            using (e) e.Set();
    }

    private static void Tick(Tracker t)
    {
        DateTime now = DateTime.UtcNow;
        IntPtr main = Native.ClaudeMainWindow();

        if (main == IntPtr.Zero)
        {
            if (Claude.IsRunning()) return; // running in the tray: nothing to do yet
            t.Window = IntPtr.Zero;
            t.Applied = false;
            if (!t.CheckedWhileClosed)
            {
                t.CheckedWhileClosed = true;
                PrepareWhileClosed();
            }
            return;
        }
        t.CheckedWhileClosed = false;

        if (main != t.Window)
        {
            t.Window = main;
            t.FirstSeen = now;
            t.Applied = false;
            t.Attempts = 0;
            t.NextTry = now;
        }
        if (t.Forced)
        {
            t.Forced = false;
            t.Applied = false;
            t.Attempts = 0;
            t.NextTry = now;
            t.FirstSeen = now.AddMinutes(-1);
        }

        if (t.Applied || t.Attempts >= MaxAttemptsPerWindow || now < t.NextTry) return;
        if (now - t.FirstSeen < TimeSpan.FromSeconds(4)) return;  // let the page finish loading

        string snippetName = Claude.RunnableSnippet();
        if (snippetName == null)
        {
            if (!t.ToldWaitingForRestart) Log.Write("snippet not installed yet; it will be added the next time Claude is closed");
            t.ToldWaitingForRestart = true;
            return;
        }

        // Only while the user is in Claude and not typing — never steal focus from elsewhere.
        if (!Native.IsForeground(main)) return;
        if (Native.IdleMilliseconds() < 900) return;
        if (Native.DevToolsWindow() != IntPtr.Zero) return; // the user has DevTools open

        Claude.TurnOnDevMode();
        t.Attempts++;
        ApplyResult result = Applier.Run(main, t.Attempts, snippetName);
        Log.Write($"apply attempt {t.Attempts} ({snippetName}): {result}");

        if (result == ApplyResult.Applied) t.Applied = true;
        else if (result == ApplyResult.NotForeground) { t.Attempts--; t.NextTry = DateTime.UtcNow.AddSeconds(1); }
        else t.NextTry = DateTime.UtcNow.AddSeconds(Math.Min(30, 3 * t.Attempts));
    }

    private static void PrepareWhileClosed()
    {
        try
        {
            if (!Claude.ProfileExists) return;
            Claude.TurnOnDevMode();
            if (Claude.SnippetStatus() != SnippetStatus.Current)
            {
                Claude.InstallSnippet();
                Log.Write("snippet installed while Claude was closed");
            }
        }
        catch (Exception ex) { Log.Write("prepare failed: " + ex.Message); }
    }
}
