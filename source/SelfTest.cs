using System.Text;
using System.Text.Json.Nodes;

namespace ClaudeDesktopRtl;

// --selftest <report>: proves the settings edits against COPIES of Claude's real files.
// Claude's own files are never touched.
internal static class SelfTest
{
    public static int Run(string reportPath)
    {
        var report = new StringBuilder();
        bool ok = true;
        void Say(string s) => report.AppendLine(s);
        void Check(string label, bool pass) { ok &= pass; Say((pass ? "  PASS  " : "  FAIL  ") + label); }

        string dir = Path.Combine(Path.GetTempPath(), "claude-desktop-rtl-selftest");
        try
        {
            Directory.CreateDirectory(dir);
            string prefs = Path.Combine(dir, "Preferences");
            string devSettings = Path.Combine(dir, "developer_settings.json");
            string real = Path.Combine(Claude.DataDir, "Preferences");
            Say("claude data folder: " + Claude.DataDir);

            if (File.Exists(real)) File.Copy(real, prefs, true);
            else File.WriteAllText(prefs, "{}");

            // Seed a user snippet and a legacy copy of ours, to prove both are handled right.
            JsonObject root = JsonNode.Parse(File.ReadAllText(prefs)).AsObject();
            JsonObject p = Obj(Obj(Obj(root, "electron"), "devtools"), "preferences");
            var seeded = new JsonArray(
                new JsonObject { ["name"] = "my-own-snippet", ["content"] = "console.log('mine')" },
                new JsonObject { ["name"] = "rtl", ["content"] = "// " + Snippet.Marker + " old build" });
            p["script-snippets"] = seeded.ToJsonString();
            File.WriteAllText(prefs, root.ToJsonString());
            var keysBefore = TopKeys(prefs);
            var prefKeysBefore = PrefKeys(prefs);

            Claude.PreferencesOverride = prefs;
            Claude.DevSettingsOverride = devSettings;

            Check("status before install is Outdated (legacy copy found)", Claude.SnippetStatus() == SnippetStatus.Outdated);
            Claude.InstallSnippet();
            Check("install: file is valid JSON", TryParse(prefs));
            Check($"install: all {keysBefore.Count} top-level keys kept", keysBefore.All(TopKeys(prefs).Contains));
            Check($"install: all {prefKeysBefore.Count} DevTools settings kept", prefKeysBefore.All(PrefKeys(prefs).Contains));
            var names = SnippetNames(prefs);
            Say("  snippets now: " + string.Join(", ", names));
            Check("install: our snippet present exactly once", names.Count(n => n == Snippet.Name) == 1);
            Check("install: legacy copy replaced", !names.Contains("rtl"));
            Check("install: the user's own snippet kept", names.Contains("my-own-snippet"));
            Check("install: status is Current", Claude.SnippetStatus() == SnippetStatus.Current);

            Claude.InstallSnippet();
            Check("install twice: still exactly once", SnippetNames(prefs).Count(n => n == Snippet.Name) == 1);

            Claude.RemoveSnippet();
            names = SnippetNames(prefs);
            Check("remove: ours gone", !names.Contains(Snippet.Name));
            Check("remove: the user's own snippet kept", names.Contains("my-own-snippet"));
            Check($"remove: all {prefKeysBefore.Count} DevTools settings kept", prefKeysBefore.All(PrefKeys(prefs).Contains));

            File.Delete(devSettings);
            DevModeSnapshot before = Claude.DevMode();
            Claude.TurnOnDevMode();
            Check("dev mode: turned on", Claude.DevMode().WasOn);
            Claude.RestoreDevMode(before);
            Check("dev mode: restored (file removed, as it never existed)", !File.Exists(devSettings));

            File.WriteAllText(devSettings, "{\"allowDevTools\":true}");
            before = Claude.DevMode();
            Claude.RestoreDevMode(before);
            Check("dev mode: left on when it was already on", Claude.DevMode().WasOn);

            Say("");
            Say(ok ? "RESULT: ALL CHECKS PASSED" : "RESULT: FAILURES ABOVE");
        }
        catch (Exception ex)
        {
            ok = false;
            Say("EXCEPTION: " + ex);
        }
        finally
        {
            Claude.PreferencesOverride = null;
            Claude.DevSettingsOverride = null;
            try { Directory.Delete(dir, true); } catch { }
            File.WriteAllText(reportPath, report.ToString());
        }
        return ok ? 0 : 1;
    }

    private static JsonObject Obj(JsonObject parent, string key)
    {
        if (parent[key] is JsonObject o) return o;
        var made = new JsonObject();
        parent[key] = made;
        return made;
    }

    private static bool TryParse(string path)
    {
        try { return JsonNode.Parse(File.ReadAllText(path)) is JsonObject; } catch { return false; }
    }

    private static List<string> TopKeys(string path) =>
        JsonNode.Parse(File.ReadAllText(path)).AsObject().Select(kv => kv.Key).ToList();

    private static List<string> PrefKeys(string path) =>
        (JsonNode.Parse(File.ReadAllText(path))["electron"]?["devtools"]?["preferences"] as JsonObject)?.Select(kv => kv.Key).ToList() ?? new List<string>();

    private static List<string> SnippetNames(string path)
    {
        string raw = JsonNode.Parse(File.ReadAllText(path))["electron"]["devtools"]["preferences"]["script-snippets"].GetValue<string>();
        return JsonNode.Parse(raw).AsArray().Select(n => n?["name"]?.GetValue<string>()).ToList();
    }
}
