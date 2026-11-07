using System.Diagnostics;
using System.Text.Json.Nodes;
using Microsoft.Win32;

namespace ClaudeDesktopRtl;

internal static class Installer
{
    private const string Footer = "To remove it completely, run “Disable Arabic RTL”.";

    // ── Enable ───────────────────────────────────────────────────────────────────
    public static int Enable(bool quiet)
    {
        using var setup = new SetupLock();

        bool running = Claude.IsRunning();
        if (!Claude.ProfileExists && !running)
        {
            if (!quiet) Dialog.Show(DialogKind.Warning, "Claude Desktop wasn't found",
                "Install the Claude desktop app and open it once, then run this again.");
            return 2;
        }

        RemoveLegacy();
        Directory.CreateDirectory(Paths.InstallDir);
        if (!File.Exists(Paths.StateFile)) State.Save(Claude.DevMode());
        Log.Write($"enable: claudeData={Claude.DataDir} running={running}");

        StopAgent();
        InstallAgentBinary();

        Claude.TurnOnDevMode();
        SnippetStatus status = Claude.SnippetStatus();
        if (status != SnippetStatus.Current && !running)
        {
            Claude.InstallSnippet();
            status = SnippetStatus.Current;
        }

        Startup.Register();
        StartAgent(applyNow: true);

        bool ready = status == SnippetStatus.Current || Claude.RunnableSnippet() != null;
        Log.Write($"enable done: snippet={status} ready={ready}");

        if (ready)
        {
            if (!quiet)
            {
                Dialog.Show(DialogKind.Success, "Arabic RTL is on",
                    running
                        ? "It’s being applied to Claude right now, silently.\n\nFrom now on it turns on by itself every time Claude opens — nothing else to do."
                        : "Open Claude — it turns on by itself.\n\nFrom now on it happens automatically every time Claude opens.",
                    Footer);
            }
            if (running) Native.ForceForeground(Native.ClaudeMainWindow());
        }
        else if (!quiet)
        {
            Dialog.Show(DialogKind.Info, "One last step",
                "Quit Claude completely (right-click its icon next to the clock → Quit), then open it again.\n\nAfter that, Arabic RTL turns on by itself every time Claude opens.",
                Footer);
        }
        return 0;
    }

    // ── Disable ──────────────────────────────────────────────────────────────────
    public static int Disable(bool quiet)
    {
        using var setup = new SetupLock();

        bool wasOn = File.Exists(Paths.AgentExe) || Startup.IsRegistered() || Claude.HasOurSnippet() || LegacyPresent();

        StopAgent();
        Startup.Unregister();
        RemoveLegacy();
        Claude.RestoreDevMode(State.Load());

        bool running = Claude.IsRunning();
        if (Claude.HasOurSnippet())
        {
            if (!running) Claude.RemoveSnippet();
            else StartDeferredCleanup();
        }

        DeleteDirectory(Paths.InstallDir);

        if (!quiet)
        {
            if (wasOn)
                Dialog.Show(DialogKind.Success, "Arabic RTL is off",
                    running
                        ? "It’s been removed and won’t start again.\n\nThe Claude window that’s open now keeps RTL until you close Claude."
                        : "It’s been removed completely.");
            else
                Dialog.Show(DialogKind.Info, "Arabic RTL is already off", "There was nothing to remove.");
        }
        return 0;
    }

    // Runs from a temporary copy after Disable when Claude was open: waits for Claude to
    // close, then takes the snippet out of its settings and deletes itself.
    public static int DeferredCleanup()
    {
        using var mutex = new Mutex(true, Names.CleanupMutex, out bool first);
        if (first)
        {
            while (true)
            {
                if (File.Exists(Paths.AgentExe) || Startup.IsRegistered()) break; // re-enabled meanwhile
                if (!Claude.IsRunning())
                {
                    try { Claude.RemoveSnippet(); } catch { }
                    break;
                }
                Thread.Sleep(3000);
            }
        }
        DeleteSelfLater();
        return 0;
    }

    // ── Pieces ───────────────────────────────────────────────────────────────────
    private static void InstallAgentBinary()
    {
        if (Paths.Same(Paths.SelfExe, Paths.AgentExe)) return;
        for (int i = 0; ; i++)
        {
            try { File.Copy(Paths.SelfExe, Paths.AgentExe, true); return; }
            catch (IOException) when (i < 10) { Thread.Sleep(300); }
        }
    }

    private static void StartAgent(bool applyNow)
    {
        Process.Start(new ProcessStartInfo(Paths.AgentExe)
        {
            Arguments = applyNow ? "--agent --apply-now" : "--agent",
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = Paths.InstallDir,
        })?.Dispose();
    }

    private static void StopAgent()
    {
        if (EventWaitHandle.TryOpenExisting(Names.QuitEvent, out EventWaitHandle quit))
            using (quit) quit.Set();

        var clock = Stopwatch.StartNew();
        while (clock.ElapsedMilliseconds < 5000 && AgentRunning()) Thread.Sleep(100);

        foreach (Process p in Native.ProcessesAt(Paths.AgentExe, Names.AgentProcessName))
            using (p) { try { p.Kill(); p.WaitForExit(3000); } catch { } }
    }

    private static bool AgentRunning()
    {
        bool any = false;
        foreach (Process p in Native.ProcessesAt(Paths.AgentExe, Names.AgentProcessName)) { any = true; p.Dispose(); }
        return any;
    }

    private static void StartDeferredCleanup()
    {
        string copy = Path.Combine(Path.GetTempPath(), "ClaudeDesktopRtl-cleanup.exe");
        try { File.Copy(Paths.SelfExe, copy, true); }
        catch (IOException) { return; } // a cleanup copy is already waiting
        Process.Start(new ProcessStartInfo(copy, "--cleanup") { UseShellExecute = false, CreateNoWindow = true })?.Dispose();
    }

    private static void DeleteSelfLater()
    {
        string self = Paths.SelfExe;
        if (self == null || !self.StartsWith(Path.GetTempPath(), StringComparison.OrdinalIgnoreCase)) return;
        Process.Start(new ProcessStartInfo("cmd.exe", $"/c ping 127.0.0.1 -n 3 >nul & del /f /q \"{self}\"")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
        })?.Dispose();
    }

    private static void DeleteDirectory(string dir)
    {
        for (int i = 0; i < 10 && Directory.Exists(dir); i++)
        {
            try { Directory.Delete(dir, true); }
            catch { Thread.Sleep(300); }
        }
    }

    // Earlier versions of this tool (tray app) left these behind.
    private static bool LegacyPresent() =>
        Directory.Exists(Paths.LegacyDataDir) || Names.LegacyRunValues.Any(Startup.HasValue);

    private static void RemoveLegacy()
    {
        foreach (string name in Names.LegacyProcessNames)
            foreach (Process p in Process.GetProcessesByName(name))
                using (p) { if (p.Id != Environment.ProcessId) try { p.Kill(); p.WaitForExit(2000); } catch { } }
        foreach (string value in Names.LegacyRunValues) Startup.DeleteValue(value);
        DeleteDirectory(Paths.LegacyDataDir);
    }

    private sealed class SetupLock : IDisposable
    {
        private readonly Mutex _mutex = new(false, Names.SetupMutex);
        private readonly bool _held;

        public SetupLock()
        {
            try { _held = _mutex.WaitOne(15000); }
            catch (AbandonedMutexException) { _held = true; }
        }

        public void Dispose()
        {
            if (_held) _mutex.ReleaseMutex();
            _mutex.Dispose();
        }
    }
}

internal static class Startup
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ApprovedKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";

    public static void Register()
    {
        using (RegistryKey k = Registry.CurrentUser.CreateSubKey(RunKey))
            k.SetValue(Names.RunValue, $"\"{Paths.AgentExe}\" --agent");
        // If it was switched off in Task Manager before, the user is turning it back on now.
        using (RegistryKey a = Registry.CurrentUser.OpenSubKey(ApprovedKey, true))
            a?.DeleteValue(Names.RunValue, false);
    }

    public static void Unregister() => DeleteValue(Names.RunValue);

    public static bool IsRegistered() => HasValue(Names.RunValue);

    public static bool HasValue(string name)
    {
        using RegistryKey k = Registry.CurrentUser.OpenSubKey(RunKey, false);
        return k?.GetValue(name) != null;
    }

    public static void DeleteValue(string name)
    {
        using (RegistryKey k = Registry.CurrentUser.OpenSubKey(RunKey, true)) k?.DeleteValue(name, false);
        using (RegistryKey a = Registry.CurrentUser.OpenSubKey(ApprovedKey, true)) a?.DeleteValue(name, false);
    }
}

// Remembers whether Developer Mode was already on before the first Enable, so Disable
// only turns off what this tool turned on.
internal static class State
{
    public static void Save(DevModeSnapshot s)
    {
        var o = new JsonObject { ["devModeFileExisted"] = s.FileExisted, ["devModeWasOn"] = s.WasOn };
        File.WriteAllText(Paths.StateFile, o.ToJsonString());
    }

    public static DevModeSnapshot Load()
    {
        try
        {
            if (!File.Exists(Paths.StateFile)) return null;
            JsonNode o = JsonNode.Parse(File.ReadAllText(Paths.StateFile));
            return new DevModeSnapshot
            {
                FileExisted = o?["devModeFileExisted"]?.GetValue<bool>() == true,
                WasOn = o?["devModeWasOn"]?.GetValue<bool>() == true,
            };
        }
        catch { return null; }
    }
}
