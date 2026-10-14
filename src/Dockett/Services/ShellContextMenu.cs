using System.Runtime.InteropServices;

namespace Dockett.Services;

/// <summary>
/// Shows Explorer's own context menu (with shell extensions like 7-Zip, Open with, Send to) for items
/// that share a parent folder. Must be called on the UI thread, which is STA.
/// </summary>
public static class ShellContextMenu
{
    static IContextMenu2? _active2;
    static IContextMenu3? _active3;
    static nint _oldWndProc;
    static readonly WndProc HookProc = Hook; // kept alive for the lifetime of the app

    /// <param name="owner">The main window handle; menu messages are routed through it while the menu is open.</param>
    /// <param name="screenX">Screen position in physical pixels.</param>
    /// <param name="extended">Include extended verbs, like Explorer does on Shift+right-click.</param>
    public static void Show(nint owner, IReadOnlyList<string> paths, int screenX, int screenY, bool extended)
    {
        if (paths.Count == 0 || owner == 0) return;

        var pidls = new List<nint>();
        IShellFolder? parent = null;
        IContextMenu? menu = null;
        var hmenu = nint.Zero;
        try
        {
            var children = new nint[paths.Count];
            for (var i = 0; i < paths.Count; i++)
            {
                Marshal.ThrowExceptionForHR(SHParseDisplayName(paths[i], 0, out var pidl, 0, out _));
                pidls.Add(pidl);
                var iid = typeof(IShellFolder).GUID;
                Marshal.ThrowExceptionForHR(SHBindToParent(pidl, ref iid, out var folder, out children[i]));
                if (i == 0)
                    parent = (IShellFolder)folder;
                else
                    Marshal.ReleaseComObject(folder);
            }

            var menuIid = typeof(IContextMenu).GUID;
            parent!.GetUIObjectOf(owner, (uint)children.Length, children, ref menuIid, 0, out var menuPtr);
            menu = (IContextMenu)Marshal.GetObjectForIUnknown(menuPtr);
            Marshal.Release(menuPtr);

            hmenu = CreatePopupMenu();
            // No CMF_CANRENAME: renaming is a shell-view feature, so that item would do nothing here.
            var flags = CMF_NORMAL | CMF_EXPLORE | (extended ? CMF_EXTENDEDVERBS : 0);
            menu.QueryContextMenu(hmenu, 0, FirstCommand, LastCommand, flags);

            // Route WM_INITMENUPOPUP and friends to the menu so submenus (Send to, Open with) fill in.
            _active2 = menu as IContextMenu2;
            _active3 = menu as IContextMenu3;
            _oldWndProc = SetWindowLongPtr(owner, GWLP_WNDPROC, Marshal.GetFunctionPointerForDelegate(HookProc));
            uint command;
            try
            {
                command = TrackPopupMenuEx(hmenu, TPM_RETURNCMD | TPM_RIGHTBUTTON, screenX, screenY, owner, 0);
            }
            finally
            {
                SetWindowLongPtr(owner, GWLP_WNDPROC, _oldWndProc);
                _active2 = null;
                _active3 = null;
            }

            if (command >= FirstCommand)
            {
                var info = new CMINVOKECOMMANDINFOEX
                {
                    cbSize = Marshal.SizeOf<CMINVOKECOMMANDINFOEX>(),
                    fMask = CMIC_MASK_UNICODE | CMIC_MASK_PTINVOKE
                            | (Keyboard(VK_CONTROL) ? CMIC_MASK_CONTROL_DOWN : 0)
                            | (Keyboard(VK_SHIFT) ? CMIC_MASK_SHIFT_DOWN : 0),
                    hwnd = owner,
                    lpVerb = (nint)(command - FirstCommand),
                    lpVerbW = (nint)(command - FirstCommand),
                    lpDirectoryW = Path.GetDirectoryName(paths[0]),
                    nShow = SW_SHOWNORMAL,
                    ptInvoke = new POINT { X = screenX, Y = screenY },
                };
                menu.InvokeCommand(ref info);
            }
        }
        finally
        {
            if (hmenu != 0) DestroyMenu(hmenu);
            if (menu is not null) Marshal.ReleaseComObject(menu);
            if (parent is not null) Marshal.ReleaseComObject(parent);
            foreach (var pidl in pidls) Marshal.FreeCoTaskMem(pidl);
        }
    }

    static nint Hook(nint hwnd, uint msg, nint wParam, nint lParam)
    {
        if (msg is WM_INITMENUPOPUP or WM_DRAWITEM or WM_MEASUREITEM or WM_MENUCHAR)
        {
            if (_active3 is not null && _active3.HandleMenuMsg2(msg, wParam, lParam, out var result) == 0)
                return result;
            if (_active2 is not null && _active2.HandleMenuMsg(msg, wParam, lParam) == 0)
                return 0;
        }
        return CallWindowProc(_oldWndProc, hwnd, msg, wParam, lParam);
    }

    static bool Keyboard(int vk) => (GetKeyState(vk) & 0x8000) != 0;

    const uint FirstCommand = 1, LastCommand = 0x7FFF;
    const uint CMF_NORMAL = 0, CMF_EXPLORE = 0x4, CMF_EXTENDEDVERBS = 0x100;
    const uint CMIC_MASK_UNICODE = 0x4000, CMIC_MASK_PTINVOKE = 0x20000000;
    const uint CMIC_MASK_SHIFT_DOWN = 0x10000000, CMIC_MASK_CONTROL_DOWN = 0x40000000;
    const uint TPM_RETURNCMD = 0x100, TPM_RIGHTBUTTON = 0x2;
    const uint WM_INITMENUPOPUP = 0x117, WM_DRAWITEM = 0x2B, WM_MEASUREITEM = 0x2C, WM_MENUCHAR = 0x120;
    const int GWLP_WNDPROC = -4, SW_SHOWNORMAL = 1, VK_SHIFT = 0x10, VK_CONTROL = 0x11;

    delegate nint WndProc(nint hwnd, uint msg, nint wParam, nint lParam);

    [StructLayout(LayoutKind.Sequential)]
    struct POINT
    {
        public int X, Y;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct CMINVOKECOMMANDINFOEX
    {
        public int cbSize;
        public uint fMask;
        public nint hwnd;
        public nint lpVerb;
        [MarshalAs(UnmanagedType.LPStr)] public string? lpParameters;
        [MarshalAs(UnmanagedType.LPStr)] public string? lpDirectory;
        public int nShow;
        public uint dwHotKey;
        public nint hIcon;
        [MarshalAs(UnmanagedType.LPStr)] public string? lpTitle;
        public nint lpVerbW;
        public string? lpParametersW;
        public string? lpDirectoryW;
        public string? lpTitleW;
        public POINT ptInvoke;
    }

    [ComImport, Guid("000214E6-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IShellFolder
    {
        void ParseDisplayName();
        void EnumObjects();
        void BindToObject();
        void BindToStorage();
        void CompareIDs();
        void CreateViewObject();
        void GetAttributesOf();
        void GetUIObjectOf(nint hwndOwner, uint cidl, [MarshalAs(UnmanagedType.LPArray)] nint[] apidl,
            ref Guid riid, nint rgfReserved, out nint ppv);
    }

    [ComImport, Guid("000214E4-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IContextMenu
    {
        [PreserveSig] int QueryContextMenu(nint hmenu, uint indexMenu, uint idCmdFirst, uint idCmdLast, uint uFlags);
        void InvokeCommand(ref CMINVOKECOMMANDINFOEX pici);
        void GetCommandString();
    }

    [ComImport, Guid("000214F4-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IContextMenu2
    {
        [PreserveSig] int QueryContextMenu(nint hmenu, uint indexMenu, uint idCmdFirst, uint idCmdLast, uint uFlags);
        void InvokeCommand(ref CMINVOKECOMMANDINFOEX pici);
        void GetCommandString();
        [PreserveSig] int HandleMenuMsg(uint uMsg, nint wParam, nint lParam);
    }

    [ComImport, Guid("BCFCE0A0-EC17-11D0-8D10-00A0C90F2719"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IContextMenu3
    {
        [PreserveSig] int QueryContextMenu(nint hmenu, uint indexMenu, uint idCmdFirst, uint idCmdLast, uint uFlags);
        void InvokeCommand(ref CMINVOKECOMMANDINFOEX pici);
        void GetCommandString();
        [PreserveSig] int HandleMenuMsg(uint uMsg, nint wParam, nint lParam);
        [PreserveSig] int HandleMenuMsg2(uint uMsg, nint wParam, nint lParam, out nint plResult);
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    static extern int SHParseDisplayName(string name, nint bindingContext, out nint pidl, uint sfgaoIn, out uint psfgaoOut);

    [DllImport("shell32.dll")]
    static extern int SHBindToParent(nint pidl, ref Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object ppv, out nint ppidlLast);

    [DllImport("user32.dll")] static extern nint CreatePopupMenu();
    [DllImport("user32.dll")] static extern bool DestroyMenu(nint hMenu);
    [DllImport("user32.dll")] static extern uint TrackPopupMenuEx(nint hmenu, uint flags, int x, int y, nint hwnd, nint lptpm);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] static extern nint SetWindowLongPtr(nint hWnd, int nIndex, nint dwNewLong);
    [DllImport("user32.dll", EntryPoint = "CallWindowProcW")] static extern nint CallWindowProc(nint prev, nint hWnd, uint msg, nint wParam, nint lParam);
    [DllImport("user32.dll")] static extern short GetKeyState(int nVirtKey);
}
