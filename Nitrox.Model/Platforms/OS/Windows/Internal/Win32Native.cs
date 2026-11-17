using System;
using System.Runtime.InteropServices;
using System.Text;

namespace Nitrox.Model.Platforms.OS.Windows.Internal;

internal static partial class Win32Native
{
    /// <summary>
    ///     https://learn.microsoft.com/en-us/windows/win32/shell/assocf_str
    /// </summary>
    [Flags]
    public enum AssocF : uint
    {
        None = 0x0,
        Init_NoRemapCLSID = 0x1,
        Init_ByExeName = 0x2,
        Open_ByExeName = 0x2,
        Init_DefaultToStar = 0x4,
        Init_DefaultToFolder = 0x8,
        NoUserSettings = 0x10,
        NoTruncate = 0x20,
        Verify = 0x40,
        RemapRunDll = 0x80,
        NoFixUps = 0x100,
        IgnoreBaseClass = 0x200,
        Init_IgnoreUnknown = 0x400,
        Init_FixedProgId = 0x800,
        IsProtocol = 0x1000,
        InitForFile = 0x2000,
        IsFullUri = 0x4000,
        PerMachineOnly = 0x8000,
        AppToApp = 0x10000
    }

    public enum AssocStr
    {
        Command = 1,
        Executable,
        FriendlyDocName,
        FriendlyAppName,
        NoOpen,
        ShellNewValue,
        DDECommand,
        DDEIfExec,
        DDEApplication,
        DDETopic,
        InfoTip,
        QuickTip,
        TileInfo,
        ContentType,
        DefaultIcon,
        ShellExtension,
        DropTarget,
        DelegateExecute,
        SupportedUriProtocols,

        // The values below ('Max' excluded) have been introduced in W10 1511
        ProgID,
        AppID,
        AppPublisher,
        AppIconReference,
        Max
    }

    /// <summary>
    ///     Gets the associated program for the file extension.
    /// </summary>
    /// <param name="str"></param>
    /// <param name="extension"></param>
    /// <returns></returns>
    public static string AssocQueryString(AssocStr str, string extension)
    {
        const int S_OK = 0;
        const int S_FALSE = 1;

        uint length = 0;
        uint ret = AssocQueryString(AssocF.None, str, extension, null, null, ref length);
        if (ret != S_FALSE)
        {
            return null;
        }

        StringBuilder sb = new((int)length);
        ret = AssocQueryString(AssocF.None, str, extension, null, sb, ref length);
        if (ret != S_OK)
        {
            return null;
        }
        return sb.ToString();
    }

    [DllImport("Shlwapi.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern uint AssocQueryString(AssocF flags, AssocStr str, string extension, string pszExtra, [Out] StringBuilder pszOut, ref uint pcchOut);

    public static string GetWineVersion()
    {
        try
        {
            return WineGetVersion();
        }
        catch (Exception)
        {
            return "";
        }
    }

    [DllImport("user32.dll", EntryPoint = "SetWindowLong")]
    internal static extern int SetWindowLong32(HandleRef hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr")]
    internal static extern IntPtr SetWindowLongPtr64(HandleRef hWnd, int nIndex, long dwNewLong);

    [return: MarshalAs(UnmanagedType.LPTStr)]
#if NET
    [LibraryImport("ntdll.dll", EntryPoint = "wine_get_version")]
    private static partial string WineGetVersion();
#else
    [DllImport("ntdll.dll", EntryPoint = "wine_get_version")]
    private static extern string WineGetVersion();
#endif

    [Flags]
    public enum WS : long
    {
        WS_BORDER = 0x00800000L,
        WS_CAPTION = 0x00C00000L,
        WS_CHILD = 0x40000000L,
        WS_CHILDWINDOW = 0x40000000L,
        WS_CLIPCHILDREN = 0x02000000L,
        WS_CLIPSIBLINGS = 0x04000000L,
        WS_DISABLED = 0x08000000L,
        WS_DLGFRAME = 0x00400000L,
        WS_GROUP = 0x00020000L,
        WS_HSCROLL = 0x00100000L,
        WS_ICONIC = 0x20000000L,
        WS_MAXIMIZE = 0x01000000L,
        WS_MAXIMIZEBOX = 0x00010000L,
        WS_MINIMIZE = 0x20000000L,
        WS_MINIMIZEBOX = 0x00020000L,
        WS_OVERLAPPED = 0x00000000L,
        WS_OVERLAPPEDWINDOW = WS_OVERLAPPED | WS_CAPTION | WS_SYSMENU | WS_THICKFRAME | WS_MINIMIZEBOX | WS_MAXIMIZEBOX,
        WS_POPUP = 0x80000000L,
        WS_POPUPWINDOW = WS_POPUP | WS_BORDER | WS_SYSMENU,
        WS_SIZEBOX = 0x00040000L,
        WS_SYSMENU = 0x00080000L,
        WS_TABSTOP = 0x00010000L,
        WS_THICKFRAME = 0x00040000L,
        WS_TILED = 0x00000000L,
        WS_TILEDWINDOW = WS_OVERLAPPED | WS_CAPTION | WS_SYSMENU | WS_THICKFRAME | WS_MINIMIZEBOX | WS_MAXIMIZEBOX,
        WS_VISIBLE = 0x10000000L,
        WS_VSCROLL = 0x00200000L
    }

}
