using System.Runtime.InteropServices;
using System.Text;

namespace Dockett.Services;

/// <summary>
/// File lists on the Windows clipboard, compatible with Explorer: CF_HDROP plus the
/// "Preferred DropEffect" flag that marks a cut (move) instead of a copy.
/// </summary>
public static class WindowsClipboard
{
    const uint CF_UNICODETEXT = 13;
    const uint CF_HDROP = 15;
    const int DROPEFFECT_COPY = 1, DROPEFFECT_MOVE = 2, DROPEFFECT_LINK = 4;
    static readonly uint CF_PREFERREDDROPEFFECT = RegisterClipboardFormat("Preferred DropEffect");

    static nint _owner;

    public static bool HasFiles => IsClipboardFormatAvailable(CF_HDROP);

    public static void SetFiles(IReadOnlyList<string> paths, bool cut)
    {
        // DROPFILES header (20 bytes) followed by a double-null-terminated list of UTF-16 paths.
        var list = Encoding.Unicode.GetBytes(string.Join('\0', paths) + "\0\0");
        var drop = GlobalAlloc(GMEM_MOVEABLE, 20 + list.Length);
        var p = GlobalLock(drop);
        Marshal.WriteInt32(p, 0, 20);  // pFiles: offset of the list
        Marshal.WriteInt64(p, 4, 0);   // pt
        Marshal.WriteInt32(p, 12, 0);  // fNC
        Marshal.WriteInt32(p, 16, 1);  // fWide
        Marshal.Copy(list, 0, p + 20, list.Length);
        GlobalUnlock(drop);

        var effect = GlobalAlloc(GMEM_MOVEABLE, 4);
        Marshal.WriteInt32(GlobalLock(effect), cut ? DROPEFFECT_MOVE : DROPEFFECT_COPY | DROPEFFECT_LINK);
        GlobalUnlock(effect);

        WithClipboard(() =>
        {
            EmptyClipboard();
            SetClipboardData(CF_HDROP, drop);
            SetClipboardData(CF_PREFERREDDROPEFFECT, effect);
        });
    }

    /// <summary>Files on the clipboard and whether they were cut, or null when there are none.</summary>
    public static (List<string> Paths, bool Cut)? GetFiles()
    {
        if (!HasFiles) return null;
        (List<string>, bool)? result = null;
        WithClipboard(() =>
        {
            var drop = GetClipboardData(CF_HDROP);
            if (drop == 0) return;
            var paths = new List<string>();
            var count = DragQueryFile(drop, uint.MaxValue, null, 0);
            for (uint i = 0; i < count; i++)
            {
                var buffer = new StringBuilder((int)DragQueryFile(drop, i, null, 0) + 1);
                DragQueryFile(drop, i, buffer, (uint)buffer.Capacity);
                paths.Add(buffer.ToString());
            }

            var cut = false;
            var effect = GetClipboardData(CF_PREFERREDDROPEFFECT);
            if (effect != 0 && GlobalLock(effect) is var pe && pe != 0)
            {
                var value = Marshal.ReadInt32(pe);
                GlobalUnlock(effect);
                cut = (value & DROPEFFECT_MOVE) != 0 && (value & DROPEFFECT_COPY) == 0;
            }
            result = (paths, cut);
        });
        return result;
    }

    public static void SetText(string text)
    {
        var bytes = Encoding.Unicode.GetBytes(text + "\0");
        var h = GlobalAlloc(GMEM_MOVEABLE, bytes.Length);
        Marshal.Copy(bytes, 0, GlobalLock(h), bytes.Length);
        GlobalUnlock(h);
        WithClipboard(() =>
        {
            EmptyClipboard();
            SetClipboardData(CF_UNICODETEXT, h);
        });
    }

    public static void Clear() => WithClipboard(() => EmptyClipboard());

    /// <summary>Opens the clipboard, retrying briefly when another app has it open.</summary>
    static void WithClipboard(Action action)
    {
        // SetClipboardData fails without an owner window, so use a message-only window.
        if (_owner == 0)
            _owner = CreateWindowEx(0, "STATIC", "DockettClipboard", 0, 0, 0, 0, 0, HWND_MESSAGE, 0, 0, 0);

        for (var attempt = 0; ; attempt++)
        {
            if (OpenClipboard(_owner)) break;
            if (attempt == 10) throw new IOException("The clipboard is in use by another app. Try again.");
            Thread.Sleep(20);
        }
        try
        {
            action();
        }
        finally
        {
            CloseClipboard();
        }
    }

    const uint GMEM_MOVEABLE = 0x0002;
    static readonly nint HWND_MESSAGE = -3;

    [DllImport("user32.dll", SetLastError = true)] static extern bool OpenClipboard(nint hWndNewOwner);
    [DllImport("user32.dll")] static extern bool CloseClipboard();
    [DllImport("user32.dll")] static extern bool EmptyClipboard();
    [DllImport("user32.dll")] static extern bool IsClipboardFormatAvailable(uint format);
    [DllImport("user32.dll")] static extern nint GetClipboardData(uint uFormat);
    [DllImport("user32.dll")] static extern nint SetClipboardData(uint uFormat, nint hMem);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern uint RegisterClipboardFormat(string lpszFormat);
    [DllImport("kernel32.dll")] static extern nint GlobalAlloc(uint uFlags, nint dwBytes);
    [DllImport("kernel32.dll")] static extern nint GlobalLock(nint hMem);
    [DllImport("kernel32.dll")] static extern bool GlobalUnlock(nint hMem);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] static extern uint DragQueryFile(nint hDrop, uint iFile, StringBuilder? lpszFile, uint cch);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern nint CreateWindowEx(uint exStyle, string className, string windowName, uint style, int x, int y,
        int width, int height, nint parent, nint menu, nint instance, nint param);
}
