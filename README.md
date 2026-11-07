# Claude Desktop RTL

Arabic right-to-left for the Claude desktop app on Windows, built from the
**Claude Arabic RTL Fix** browser extension.

> **Unofficial.** This is an independent community project. It is not affiliated with,
> endorsed by or supported by Anthropic. "Claude" is a trademark of Anthropic.

**[Download the latest release](https://github.com/aly-abdullah/claude-desktop-rtl/releases/latest)**

| Double-click | What happens |
|---|---|
| **Enable Arabic RTL.exe** | Turns it on. It is applied right away and again every time Claude opens, including after a restart of Windows. |
| **Disable Arabic RTL.exe** | Turns it off and removes everything it added. |

Nothing to install. Each file is self-contained, so copy both to any 64-bit Windows PC and
double-click. Nothing else is needed.

## What you will see

Nothing, apart from Arabic flowing right-to-left and the RTL / Aa / Headers buttons next to
the model name in Claude. There's no window or tray icon, and no DevTools flash.

The first time on a new PC, if Claude is open and has never had the fix, the tool asks you
to quit Claude once (tray icon → Quit) and reopen it. After that it is automatic.

## How it works

Claude Desktop blocks other programs from injecting code into it (signed package, Electron
fuses, a signed-token gate on remote debugging), and this tool does not work around any of
that. It uses only what the app offers its own users:

1. **Developer Mode.** `developer_settings.json` → `{"allowDevTools": true}`, the same
   switch as *Help → Troubleshooting → Enable Developer Mode*.
2. **A saved DevTools snippet** named `claude-desktop-rtl`, written into Claude's
   `Preferences` while Claude is closed. Other snippets and settings are left untouched, and
   the file is backed up once to `Preferences.before-arabic-rtl`.
3. **A small background agent** (`%LOCALAPPDATA%\ClaudeDesktopRtl\ClaudeDesktopRtl.exe`,
   started with Windows). When Claude opens, and only while you are in Claude and not
   typing, it presses Claude's own DevTools shortcut and runs the snippet. The DevTools
   window is made fully transparent and click-through the moment it is created, so you
   never see it. Every keystroke is sent only while that window is confirmed in front, and
   the run stops if you touch the keyboard. Nothing can land in Claude's message box.

Claude's data folder has moved between versions (`Roaming\Claude`, `Local\Claude`,
`Local\Claude-Data\claude_…`). The tool reads it from the running app and never assumes
a location.

## Rebuilding after a new extension version

Double-click `source\Build.cmd`. It regenerates the script from the newest `vN …` folder of the
extension and rebuilds both executables. By default it looks for a `Claude Arabic RTL Fix` folder
next to this repository; pass `-ProjectRoot` or `-ExtensionDir` to `source\snippet\make-snippet.ps1`
to point somewhere else. If the extension folder is not found, the existing script is kept. Then run
**Enable Arabic RTL.exe** once on each PC; the new script is picked up the next time Claude
is closed.

## Troubleshooting

- Log: `%LOCALAPPDATA%\ClaudeDesktopRtl\log.txt`.
- `"Enable Arabic RTL.exe" --selftest` checks the settings edits against copies of Claude's
  files. Your real files are not touched.
- `"Enable Arabic RTL.exe" --diag` runs one apply now. Add `--visible` to watch it with
  DevTools showing.
- Double-clicking **Enable** again re-applies immediately, for example after Claude reloads.

## License

[MIT](LICENSE) © 2026 Aly Abdullah
