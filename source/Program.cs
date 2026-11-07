namespace ClaudeDesktopRtl;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        string mode = args.Length > 0 ? args[0] : "";
        bool quiet = args.Contains("--quiet");

        try
        {
            switch (mode)
            {
                case "--agent":
                    return Agent.Run(applyNow: args.Contains("--apply-now"));
                case "--cleanup":
                    return Installer.DeferredCleanup();
                case "--diag":
                    Applier.Visible = args.Contains("--visible");
                    IntPtr main = Native.ClaudeMainWindow();
                    Native.ForceForeground(main);
                    Thread.Sleep(800);
                    Log.Write("diag: " + Applier.Run(main, 1, Claude.RunnableSnippet() ?? Snippet.Name));
                    return 0;
                case "--selftest":
                    return SelfTest.Run(args.Length > 1 ? args[1] : Path.Combine(Path.GetTempPath(), "claude-desktop-rtl-selftest.txt"));
            }

#if DISABLE_BUILD
            return Installer.Disable(quiet);
#else
            return Installer.Enable(quiet);
#endif
        }
        catch (Exception ex)
        {
            Log.Write("fatal: " + ex);
            if (!quiet && !mode.StartsWith("--"))
                Dialog.Show(DialogKind.Error, "Something went wrong", ex.Message);
            return 1;
        }
    }
}
