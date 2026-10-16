using System.Runtime.InteropServices;

namespace RoG.App.Services;

/// <summary>
/// Symbol im Infobereich (Tray) über <c>Shell_NotifyIcon</c> mit einem unsichtbaren Nachrichtenfenster – ohne
/// Fremdbibliothek, damit es keine Versionskonflikte mit dem Windows App SDK gibt. Linksklick öffnet die App,
/// Rechtsklick zeigt das Menü. Muss im UI-Thread erzeugt werden (dessen Nachrichtenschleife bedient das Fenster).
/// </summary>
internal sealed unsafe partial class TrayIcon : IDisposable
{
    private const int WmApp = 0x8000;
    private const int CallbackMessage = WmApp + 1;
    private const int WmLButtonUp = 0x0202;
    private const int WmRButtonUp = 0x0205;
    private const int WmContextMenu = 0x007B;
    private const uint NimAdd = 0, NimModify = 1, NimDelete = 2, NimSetVersion = 4;
    private const uint NifMessage = 0x1, NifIcon = 0x2, NifTip = 0x4, NifShowTip = 0x80;
    private const uint MfString = 0x0, MfSeparator = 0x800;
    private const uint TpmReturnCmd = 0x100, TpmRightButton = 0x2, TpmBottomAlign = 0x20;
    private const uint ImageIcon = 1, LrLoadFromFile = 0x10, LrDefaultSize = 0x40;
    private static readonly nint s_hwndMessage = -3;

    // Delegat muss leben, solange das Fenster existiert (sonst sammelt der GC den Funktionszeiger ein)
    private readonly WndProcDelegate _wndProc;
    private readonly string _className = "RoGTrayWindow_" + Guid.NewGuid().ToString("N");
    private readonly nint _classNamePtr;
    private readonly uint _taskbarCreated;
    private readonly nint _icon;
    private nint _hwnd;
    private string _tooltip;
    private bool _added;

    public TrayIcon(string iconPath, string tooltip)
    {
        _tooltip = tooltip;
        _wndProc = WndProc;
        _classNamePtr = Marshal.StringToHGlobalUni(_className);
        var wc = new WndClassEx
        {
            CbSize = (uint)sizeof(WndClassEx),
            LpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProc),
            HInstance = GetModuleHandleW(null),
            LpszClassName = _classNamePtr,
        };
        _ = RegisterClassExW(ref wc);
        _hwnd = CreateWindowExW(0, _classNamePtr, _classNamePtr, 0, 0, 0, 0, 0, s_hwndMessage, 0, wc.HInstance, 0);
        _taskbarCreated = RegisterWindowMessageW("TaskbarCreated");
        _icon = LoadImageW(0, iconPath, ImageIcon, 0, 0, LrLoadFromFile | LrDefaultSize);
        Add();
    }

    private delegate nint WndProcDelegate(nint hwnd, uint msg, nint wParam, nint lParam);

    /// <summary>Linksklick auf das Symbol.</summary>
    public event EventHandler? Activated;

    /// <summary>Gewählter Menüeintrag (Index in der Liste von <see cref="MenuItems"/>).</summary>
    public event EventHandler<int>? MenuItemInvoked;

    /// <summary>Menüeinträge; <c>null</c> = Trennlinie.</summary>
    public IReadOnlyList<string?> MenuItems { get; set; } = [];

    public void SetTooltip(string tooltip)
    {
        _tooltip = tooltip;
        if (_added)
        {
            var data = Data(NifTip | NifShowTip);
            _ = Shell_NotifyIconW(NimModify, &data);
        }
    }

    private void Add()
    {
        if (_hwnd == 0)
        {
            return;
        }

        var data = Data(NifMessage | NifIcon | NifTip | NifShowTip);
        _added = Shell_NotifyIconW(NimAdd, &data) != 0;
        data.UTimeoutOrVersion = 4; // NOTIFYICON_VERSION_4
        _ = Shell_NotifyIconW(NimSetVersion, &data);
    }

    private NotifyIconData Data(uint flags)
    {
        var data = new NotifyIconData
        {
            CbSize = (uint)sizeof(NotifyIconData),
            HWnd = _hwnd,
            UId = 1,
            UFlags = flags,
            UCallbackMessage = CallbackMessage,
            HIcon = _icon,
        };
        var tip = _tooltip.Length > 127 ? _tooltip[..127] : _tooltip;
        for (var i = 0; i < tip.Length; i++)
        {
            data.SzTip[i] = tip[i];
        }

        return data;
    }

    private nint WndProc(nint hwnd, uint msg, nint wParam, nint lParam)
    {
        if (msg == CallbackMessage)
        {
            // NOTIFYICON_VERSION_4: Ereignis im unteren Wort von lParam
            switch ((int)(lParam & 0xFFFF))
            {
                case WmLButtonUp:
                    Activated?.Invoke(this, EventArgs.Empty);
                    break;
                case WmRButtonUp or WmContextMenu:
                    ShowMenu();
                    break;
            }

            return 0;
        }

        if (msg == _taskbarCreated && _taskbarCreated != 0)
        {
            // Explorer neu gestartet – Symbol neu anlegen
            Add();
            return 0;
        }

        return DefWindowProcW(hwnd, msg, wParam, lParam);
    }

    private void ShowMenu()
    {
        var menu = CreatePopupMenu();
        if (menu == 0)
        {
            return;
        }

        try
        {
            for (var i = 0; i < MenuItems.Count; i++)
            {
                if (MenuItems[i] is { } text)
                {
                    _ = AppendMenuW(menu, MfString, (nuint)(i + 1), text);
                }
                else
                {
                    _ = AppendMenuW(menu, MfSeparator, 0, null);
                }
            }

            _ = GetCursorPos(out var point);
            _ = SetForegroundWindow(_hwnd); // sonst schließt das Menü nicht beim Klick daneben
            var command = TrackPopupMenuEx(menu, TpmReturnCmd | TpmRightButton | TpmBottomAlign, point.X, point.Y, _hwnd, 0);
            if (command > 0)
            {
                MenuItemInvoked?.Invoke(this, command - 1);
            }
        }
        finally
        {
            _ = DestroyMenu(menu);
        }
    }

    public void Dispose()
    {
        if (_added)
        {
            var data = Data(0);
            _ = Shell_NotifyIconW(NimDelete, &data);
            _added = false;
        }

        if (_icon != 0)
        {
            _ = DestroyIcon(_icon);
        }

        if (_hwnd != 0)
        {
            _ = DestroyWindow(_hwnd);
            _hwnd = 0;
        }

        _ = UnregisterClassW(_classNamePtr, GetModuleHandleW(null));
        Marshal.FreeHGlobal(_classNamePtr);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WndClassEx
    {
        public uint CbSize;
        public uint Style;
        public nint LpfnWndProc;
        public int CbClsExtra;
        public int CbWndExtra;
        public nint HInstance;
        public nint HIcon;
        public nint HCursor;
        public nint HbrBackground;
        public nint LpszMenuName;
        public nint LpszClassName;
        public nint HIconSm;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NotifyIconData
    {
        public uint CbSize;
        public nint HWnd;
        public uint UId;
        public uint UFlags;
        public uint UCallbackMessage;
        public nint HIcon;
        public fixed char SzTip[128];
        public uint DwState;
        public uint DwStateMask;
        public fixed char SzInfo[256];
        public uint UTimeoutOrVersion;
        public fixed char SzInfoTitle[64];
        public uint DwInfoFlags;
        public Guid GuidItem;
        public nint HBalloonIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X;
        public int Y;
    }

    [LibraryImport("shell32.dll")]
    private static partial int Shell_NotifyIconW(uint message, NotifyIconData* data);

    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial ushort RegisterClassExW(ref WndClassEx wc);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool UnregisterClassW(nint className, nint instance);

    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial nint CreateWindowExW(uint exStyle, nint className, nint windowName, uint style, int x, int y, int width, int height, nint parent, nint menu, nint instance, nint param);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DestroyWindow(nint hwnd);

    [LibraryImport("user32.dll")]
    private static partial nint DefWindowProcW(nint hwnd, uint msg, nint wParam, nint lParam);

    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial uint RegisterWindowMessageW(string name);

    [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint GetModuleHandleW(string? name);

    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    private static partial nint LoadImageW(nint instance, string name, uint type, int cx, int cy, uint load);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DestroyIcon(nint icon);

    [LibraryImport("user32.dll")]
    private static partial nint CreatePopupMenu();

    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AppendMenuW(nint menu, uint flags, nuint id, string? text);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DestroyMenu(nint menu);

    [LibraryImport("user32.dll")]
    private static partial int TrackPopupMenuEx(nint menu, uint flags, int x, int y, nint hwnd, nint tpm);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetCursorPos(out Point point);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetForegroundWindow(nint hwnd);
}
