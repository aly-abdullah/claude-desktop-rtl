using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ClaudeDesktopRtl;

internal enum SnippetStatus { Missing, Outdated, Current }

internal sealed class DevModeSnapshot
{
    public bool FileExisted;
    public bool WasOn;
}

// Claude keeps two plain files we rely on, both in its own data folder:
//   developer_settings.json  {"allowDevTools": true}  re-read on every Ctrl+Alt+I
//   Preferences  electron.devtools.preferences["script-snippets"]  (a JSON string)
// Preferences belongs to the running app and is rewritten when it exits, so it is
// only ever written while Claude is closed.
internal static class Claude
{
    internal static string PreferencesOverride;
    internal static string DevSettingsOverride;

    private static readonly JsonSerializerOptions Compact = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    public static string DataDir => Paths.ClaudeDataDir;
    private static string DevSettingsPath => DevSettingsOverride ?? Path.Combine(DataDir, "developer_settings.json");
    private static string PreferencesPath => PreferencesOverride ?? Path.Combine(DataDir, "Preferences");

    public static bool ProfileExists => Directory.Exists(DataDir);

    public static bool IsRunning() => Native.DesktopPids().Count > 0;

    // ── Developer Mode ───────────────────────────────────────────────────────────
    public static DevModeSnapshot DevMode()
    {
        var snapshot = new DevModeSnapshot { FileExisted = File.Exists(DevSettingsPath) };
        if (snapshot.FileExisted)
        {
            try { snapshot.WasOn = JsonNode.Parse(File.ReadAllText(DevSettingsPath))?["allowDevTools"]?.GetValue<bool>() == true; }
            catch { }
        }
        return snapshot;
    }

    public static void TurnOnDevMode()
    {
        if (DevMode().WasOn) return;
        JsonObject root = ReadObjectOrNull(DevSettingsPath) ?? new JsonObject();
        root["allowDevTools"] = true;
        Directory.CreateDirectory(Path.GetDirectoryName(DevSettingsPath));
        WriteAtomic(DevSettingsPath, root.ToJsonString(Indented));
    }

    public static void RestoreDevMode(DevModeSnapshot before)
    {
        if (before == null || before.WasOn || !File.Exists(DevSettingsPath)) return;
        JsonObject root = ReadObjectOrNull(DevSettingsPath) ?? new JsonObject();
        root.Remove("allowDevTools");
        if (!before.FileExisted && root.Count == 0) File.Delete(DevSettingsPath);
        else WriteAtomic(DevSettingsPath, root.ToJsonString(Indented));
    }

    // ── The DevTools snippet ─────────────────────────────────────────────────────
    public static SnippetStatus SnippetStatus()
    {
        JsonObject prefs = DevToolsPrefs(ReadPreferences(), create: false);
        if (prefs == null) return ClaudeDesktopRtl.SnippetStatus.Missing;

        bool outdated = false;
        foreach (JsonNode item in SnippetList(prefs))
        {
            if (!IsOurs(item)) continue;
            if (NameOf(item) == Snippet.Name && ContentOf(item) == Snippet.Content) return ClaudeDesktopRtl.SnippetStatus.Current;
            outdated = true;
        }
        return outdated ? ClaudeDesktopRtl.SnippetStatus.Outdated : ClaudeDesktopRtl.SnippetStatus.Missing;
    }

    // The snippet name to run right now: ours, or an older copy saved by a previous
    // version (it is replaced the next time Claude is closed). Null = none available.
    public static string RunnableSnippet()
    {
        try
        {
            JsonObject prefs = DevToolsPrefs(ReadPreferences(), create: false);
            if (prefs == null) return null;
            var list = SnippetList(prefs);
            if (list.Any(i => NameOf(i) == Snippet.Name)) return Snippet.Name;
            if (list.Any(IsOurs)) return Snippet.LegacyName;
        }
        catch { }
        return null;
    }

    public static bool HasOurSnippet()
    {
        try
        {
            JsonObject prefs = DevToolsPrefs(ReadPreferences(), create: false);
            return prefs != null && SnippetList(prefs).Any(IsOurs);
        }
        catch { return false; }
    }

    public static void InstallSnippet()
    {
        JsonObject root = ReadPreferences() ?? new JsonObject();
        JsonObject prefs = DevToolsPrefs(root, create: true);

        var kept = new JsonArray();
        foreach (JsonNode item in SnippetList(prefs))
            if (item != null && !IsOurs(item)) kept.Add(item.DeepClone());
        kept.Add((JsonNode)new JsonObject { ["name"] = Snippet.Name, ["content"] = Snippet.Content });

        prefs["script-snippets"] = kept.ToJsonString(Compact);
        Save(root);
    }

    public static void RemoveSnippet()
    {
        JsonObject root = ReadPreferences();
        JsonObject prefs = DevToolsPrefs(root, create: false);
        if (prefs == null) return;

        JsonArray all = SnippetList(prefs);
        var kept = new JsonArray();
        foreach (JsonNode item in all)
            if (item != null && !IsOurs(item)) kept.Add(item.DeepClone());
        if (kept.Count == all.Count) return;

        prefs["script-snippets"] = kept.ToJsonString(Compact);
        Save(root);
    }

    private static bool IsOurs(JsonNode item)
    {
        string name = NameOf(item);
        if (name == Snippet.Name) return true;
        return name == Snippet.LegacyName && (ContentOf(item) ?? "").Contains(Snippet.Marker);
    }

    private static string NameOf(JsonNode item)
    {
        try { return item?["name"]?.GetValue<string>(); } catch { return null; }
    }

    private static string ContentOf(JsonNode item)
    {
        try { return item?["content"]?.GetValue<string>(); } catch { return null; }
    }

    private static JsonArray SnippetList(JsonObject prefs)
    {
        try
        {
            string raw = prefs?["script-snippets"]?.GetValue<string>();
            if (!string.IsNullOrWhiteSpace(raw) && JsonNode.Parse(raw) is JsonArray list) return list;
        }
        catch { }
        return new JsonArray();
    }

    private static JsonObject DevToolsPrefs(JsonObject root, bool create)
    {
        if (root == null) return null;
        JsonObject Child(JsonObject parent, string key)
        {
            if (parent[key] is JsonObject existing) return existing;
            if (!create) return null;
            var made = new JsonObject();
            parent[key] = made;
            return made;
        }
        JsonObject electron = Child(root, "electron");
        JsonObject devtools = electron == null ? null : Child(electron, "devtools");
        return devtools == null ? null : Child(devtools, "preferences");
    }

    // Never overwrite a Preferences file we cannot read: that would wipe Claude's settings.
    private static JsonObject ReadPreferences()
    {
        if (!File.Exists(PreferencesPath)) return null;
        return JsonNode.Parse(File.ReadAllText(PreferencesPath)) as JsonObject
            ?? throw new InvalidDataException("Claude's Preferences file has an unexpected format.");
    }

    private static void Save(JsonObject root)
    {
        string backup = PreferencesPath + ".before-arabic-rtl";
        try { if (File.Exists(PreferencesPath) && !File.Exists(backup)) File.Copy(PreferencesPath, backup); } catch { }
        Directory.CreateDirectory(Path.GetDirectoryName(PreferencesPath));
        WriteAtomic(PreferencesPath, root.ToJsonString(Compact));
    }

    private static JsonObject ReadObjectOrNull(string path)
    {
        try { return File.Exists(path) ? JsonNode.Parse(File.ReadAllText(path)) as JsonObject : null; }
        catch { return null; }
    }

    private static void WriteAtomic(string path, string text)
    {
        string tmp = path + ".arabic-rtl.tmp";
        File.WriteAllText(tmp, text, new UTF8Encoding(false));
        File.Move(tmp, path, true);
    }
}
