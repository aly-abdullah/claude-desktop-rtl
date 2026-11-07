using System.Text;

namespace ClaudeDesktopRtl;

internal static class Names
{
    public const string AgentMutex = @"Local\ClaudeDesktopRtl.Agent";
    public const string SetupMutex = @"Local\ClaudeDesktopRtl.Setup";
    public const string CleanupMutex = @"Local\ClaudeDesktopRtl.Cleanup";
    public const string QuitEvent = @"Local\ClaudeDesktopRtl.Quit";
    public const string ApplyNowEvent = @"Local\ClaudeDesktopRtl.ApplyNow";

    public const string RunValue = "ClaudeDesktopRtl";
    public static readonly string[] LegacyRunValues = { "ClaudeArabicRtlTray", "ClaudeArabicRtl" };
    public static readonly string[] LegacyProcessNames = { "ClaudeArabicRtlTray", "Claude Arabic RTL" };

    public const string AgentExeName = "ClaudeDesktopRtl.exe";
    public const string AgentProcessName = "ClaudeDesktopRtl";
}

internal static class Paths
{
    private static string Env(string name)
    {
        string v = Environment.GetEnvironmentVariable(name);
        return string.IsNullOrWhiteSpace(v) ? null : v;
    }

    private static string Known(Environment.SpecialFolder folder)
    {
        try
        {
            string v = Environment.GetFolderPath(folder);
            return string.IsNullOrWhiteSpace(v) ? null : v;
        }
        catch { return null; }
    }

    private static string UserProfile => Env("USERPROFILE") ?? Known(Environment.SpecialFolder.UserProfile) ?? "";

    // The environment variables come first: they are what Claude itself uses, and
    // GetFolderPath has been seen returning the wrong folder depending on how a
    // process was launched.
    public static string LocalAppData =>
        Env("LOCALAPPDATA") ?? Known(Environment.SpecialFolder.LocalApplicationData) ?? Path.Combine(UserProfile, "AppData", "Local");

    public static string RoamingAppData =>
        Env("APPDATA") ?? Known(Environment.SpecialFolder.ApplicationData) ?? Path.Combine(UserProfile, "AppData", "Roaming");

    public static string InstallDir => Path.Combine(LocalAppData, "ClaudeDesktopRtl");
    public static string AgentExe => Path.Combine(InstallDir, Names.AgentExeName);
    public static string StateFile => Path.Combine(InstallDir, "state.json");
    public static string LogFile => Path.Combine(InstallDir, "log.txt");
    public static string LegacyDataDir => Path.Combine(RoamingAppData, "ClaudeArabicRtlTray");
    public static string SelfExe => Environment.ProcessPath;

    // Claude has moved its data folder between versions (Roaming\Claude, Local\Claude,
    // Local\Claude-Data\claude_<id>), so it is never assumed. While Claude runs, the
    // folder is read from its own --user-data-dir; otherwise the candidate whose
    // settings were written most recently wins.
    private static string _dataDir;
    private static DateTime _dataDirAt;

    public static string ClaudeDataDir
    {
        get
        {
            if (_dataDir != null && DateTime.UtcNow - _dataDirAt < TimeSpan.FromSeconds(5)) return _dataDir;
            _dataDir = Native.RunningClaudeDataDir() ?? NewestDataDirCandidate();
            _dataDirAt = DateTime.UtcNow;
            return _dataDir;
        }
    }

    private static string NewestDataDirCandidate()
    {
        var candidates = new List<string>();
        void Add(string p) { if (!string.IsNullOrWhiteSpace(p) && Directory.Exists(p)) candidates.Add(p); }
        void AddChildren(string parent, string pattern)
        {
            try { if (Directory.Exists(parent)) foreach (string d in Directory.GetDirectories(parent, pattern)) Add(d); }
            catch { }
        }

        AddChildren(Path.Combine(LocalAppData, "Claude-Data"), "*");
        Add(Path.Combine(LocalAppData, "Claude"));
        Add(Path.Combine(RoamingAppData, "Claude"));
        try
        {
            string packages = Path.Combine(LocalAppData, "Packages");
            if (Directory.Exists(packages))
                foreach (string pkg in Directory.GetDirectories(packages, "Claude_*"))
                {
                    Add(Path.Combine(pkg, "LocalCache", "Roaming", "Claude"));
                    Add(Path.Combine(pkg, "LocalCache", "Local", "Claude"));
                }
        }
        catch { }

        DateTime Stamp(string dir)
        {
            DateTime best = DateTime.MinValue;
            foreach (string f in new[] { "Preferences", "config.json", "Local State" })
            {
                string p = Path.Combine(dir, f);
                if (File.Exists(p)) { DateTime t = File.GetLastWriteTimeUtc(p); if (t > best) best = t; }
            }
            return best;
        }

        string newest = candidates.Distinct(StringComparer.OrdinalIgnoreCase)
                                  .Select(d => (dir: d, at: Stamp(d)))
                                  .Where(x => x.at > DateTime.MinValue)
                                  .OrderByDescending(x => x.at)
                                  .Select(x => x.dir)
                                  .FirstOrDefault();
        return newest ?? Path.Combine(RoamingAppData, "Claude");
    }

    public static bool Same(string a, string b) =>
        a != null && b != null &&
        string.Equals(Path.GetFullPath(a).TrimEnd('\\'), Path.GetFullPath(b).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);
}

internal static class Log
{
    private static readonly object Gate = new();

    public static void Write(string message)
    {
        try
        {
            if (!Directory.Exists(Paths.InstallDir)) return;
            lock (Gate)
            {
                var file = new FileInfo(Paths.LogFile);
                if (file.Exists && file.Length > 256 * 1024) File.Move(file.FullName, file.FullName + ".old", true);
                File.AppendAllText(Paths.LogFile,
                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}  [{Environment.ProcessId}] {message}{Environment.NewLine}",
                    new UTF8Encoding(false));
            }
        }
        catch { }
    }
}

internal static class Snippet
{
    public const string Name = "claude-desktop-rtl";
    public const string LegacyName = "rtl";
    public const string Marker = "Claude Arabic RTL Fix";

    private static string _content;
    public static string Content => _content ??= Load();

    private static string Load()
    {
        using Stream s = typeof(Snippet).Assembly.GetManifestResourceStream("claude-rtl-snippet.js")
            ?? throw new InvalidOperationException("The RTL script is missing from this build.");
        using var r = new StreamReader(s, Encoding.UTF8);
        return r.ReadToEnd().Replace("\r\n", "\n");
    }
}
