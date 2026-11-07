using System.Runtime.InteropServices;

namespace ClaudeDesktopRtl;

internal enum DialogKind { Success, Info, Warning, Error }

// A native Windows task dialog (the modern, crisp one), carrying this app's own icon.
internal static class Dialog
{
    private const string Title = "Claude Desktop RTL";

    private const uint TDF_USE_HICON_MAIN = 0x0002;
    private const uint TDF_ALLOW_DIALOG_CANCELLATION = 0x0008;
    private const uint TDF_SIZE_TO_CONTENT = 0x01000000;
    private const uint TDCBF_OK_BUTTON = 0x0001;
    private const int TD_WARNING_ICON = -1, TD_ERROR_ICON = -2, TD_INFORMATION_ICON = -3;
    private const int SM_CXICON = 11;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode, Pack = 1)]
    private struct TASKDIALOGCONFIG
    {
        public uint cbSize;
        public IntPtr hwndParent;
        public IntPtr hInstance;
        public uint dwFlags;
        public uint dwCommonButtons;
        [MarshalAs(UnmanagedType.LPWStr)] public string pszWindowTitle;
        public IntPtr hMainIcon;
        [MarshalAs(UnmanagedType.LPWStr)] public string pszMainInstruction;
        [MarshalAs(UnmanagedType.LPWStr)] public string pszContent;
        public uint cButtons;
        public IntPtr pButtons;
        public int nDefaultButton;
        public uint cRadioButtons;
        public IntPtr pRadioButtons;
        public int nDefaultRadioButton;
        [MarshalAs(UnmanagedType.LPWStr)] public string pszVerificationText;
        [MarshalAs(UnmanagedType.LPWStr)] public string pszExpandedInformation;
        [MarshalAs(UnmanagedType.LPWStr)] public string pszExpandedControlText;
        [MarshalAs(UnmanagedType.LPWStr)] public string pszCollapsedControlText;
        public IntPtr hFooterIcon;
        [MarshalAs(UnmanagedType.LPWStr)] public string pszFooter;
        public IntPtr pfCallback;
        public IntPtr lpCallbackData;
        public uint cxWidth;
    }

    [DllImport("comctl32.dll", CharSet = CharSet.Unicode)]
    private static extern int TaskDialogIndirect(ref TASKDIALOGCONFIG config, out int button, out int radio, out bool verified);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern uint PrivateExtractIcons(string file, int index, int cx, int cy, IntPtr[] icons, IntPtr[] ids, uint count, uint flags);

    [DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr icon);
    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int MessageBox(IntPtr hWnd, string text, string caption, uint type);

    public static void Show(DialogKind kind, string heading, string body, string footer = null)
    {
        IntPtr icon = IntPtr.Zero;
        try
        {
            var config = new TASKDIALOGCONFIG
            {
                cbSize = (uint)Marshal.SizeOf<TASKDIALOGCONFIG>(),
                dwFlags = TDF_ALLOW_DIALOG_CANCELLATION | TDF_SIZE_TO_CONTENT,
                dwCommonButtons = TDCBF_OK_BUTTON,
                pszWindowTitle = Title,
                pszMainInstruction = heading,
                pszContent = body,
                pszFooter = footer,
                hFooterIcon = footer == null ? IntPtr.Zero : new IntPtr(unchecked((ushort)TD_INFORMATION_ICON)),
            };

            if ((kind is DialogKind.Success or DialogKind.Info) && (icon = AppIcon()) != IntPtr.Zero)
            {
                config.dwFlags |= TDF_USE_HICON_MAIN;
                config.hMainIcon = icon;
            }
            else
            {
                int id = kind switch { DialogKind.Warning => TD_WARNING_ICON, DialogKind.Error => TD_ERROR_ICON, _ => TD_INFORMATION_ICON };
                config.hMainIcon = new IntPtr(unchecked((ushort)id));
            }

            if (TaskDialogIndirect(ref config, out _, out _, out _) == 0) return;
        }
        catch (Exception ex) when (ex is EntryPointNotFoundException or DllNotFoundException or MarshalDirectiveException) { }
        finally
        {
            if (icon != IntPtr.Zero) DestroyIcon(icon);
        }

        uint style = kind switch { DialogKind.Warning => 0x30u, DialogKind.Error => 0x10u, _ => 0x40u };
        MessageBox(IntPtr.Zero, footer == null ? body : body + "\n\n" + footer, heading, style | 0x10000u);
    }

    private static IntPtr AppIcon()
    {
        try
        {
            int size = Math.Max(32, GetSystemMetrics(SM_CXICON));
            var icons = new IntPtr[1];
            return PrivateExtractIcons(Paths.SelfExe, 0, size, size, icons, null, 1, 0) == 1 ? icons[0] : IntPtr.Zero;
        }
        catch { return IntPtr.Zero; }
    }
}
