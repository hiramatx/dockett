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
        if (paths.Count == 0 || paths.Any(p => string.IsNullOrEmpty(p) || p.Contains('\0')))
            throw new ArgumentException("Nothing to put on the clipboard.", nameof(paths));

        var list = Encoding.Unicode.GetBytes(string.Join('\0', paths) + "\0\0");
        var header = new byte[20];
        BitConverter.TryWriteBytes(header.AsSpan(0), 20);  // pFiles: offset of the list; pt and fNC stay zero
        BitConverter.TryWriteBytes(header.AsSpan(16), 1);  // fWide
        var effect = BitConverter.GetBytes(cut ? DROPEFFECT_MOVE : DROPEFFECT_COPY | DROPEFFECT_LINK);

        Put((CF_HDROP, [.. header, .. list]), (CF_PREFERREDDROPEFFECT, effect));
    }

    /// <summary>
    /// Replaces the clipboard contents. The clipboard takes ownership of each block it accepts;
    /// blocks it doesn't take (allocation, open, or set failed) are freed here instead of leaking.
    /// </summary>
    static void Put(params (uint Format, byte[] Data)[] entries)
    {
        var handles = new nint[entries.Length];
        try
        {
            for (var i = 0; i < entries.Length; i++)
                handles[i] = Allocate(entries[i].Data);

            WithClipboard(() =>
            {
                if (!EmptyClipboard())
                    throw new IOException("Couldn't clear the clipboard. Try again.");
                for (var i = 0; i < entries.Length; i++)
                {
                    if (SetClipboardData(entries[i].Format, handles[i]) == 0)
                        throw new IOException("Couldn't put that on the clipboard. Try again.");
                    handles[i] = 0; // now owned by the clipboard
                }
            });
        }
        finally
        {
            foreach (var handle in handles)
            {
                if (handle != 0) GlobalFree(handle);
            }
        }
    }

    static nint Allocate(byte[] data)
    {
        var handle = GlobalAlloc(GMEM_MOVEABLE, data.Length);
        if (handle == 0)
            throw new IOException("Not enough memory to use the clipboard.");
        var p = GlobalLock(handle);
        if (p == 0)
        {
            GlobalFree(handle);
            throw new IOException("Not enough memory to use the clipboard.");
        }
        Marshal.Copy(data, 0, p, data.Length);
        GlobalUnlock(handle);
        return handle;
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
                // Any app can put a file list here. Only complete paths are safe to act on.
                if (ShellOperations.IsCompletePath(buffer.ToString()))
                    paths.Add(buffer.ToString());
            }

            var cut = false;
            var effect = GetClipboardData(CF_PREFERREDDROPEFFECT);
            if (effect != 0 && GlobalSize(effect) >= sizeof(int) && GlobalLock(effect) is var pe && pe != 0)
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
        // A null would cut the text short for whoever pastes it.
        Put((CF_UNICODETEXT, Encoding.Unicode.GetBytes(text.Replace("\0", "") + "\0")));
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
    [DllImport("kernel32.dll")] static extern nint GlobalFree(nint hMem);
    [DllImport("kernel32.dll")] static extern nuint GlobalSize(nint hMem);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] static extern uint DragQueryFile(nint hDrop, uint iFile, StringBuilder? lpszFile, uint cch);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern nint CreateWindowEx(uint exStyle, string className, string windowName, uint style, int x, int y,
        int width, int height, nint parent, nint menu, nint instance, nint param);
}
