using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using Dockett.Models;

namespace Dockett.Services;

/// <summary>
/// Real Windows icons from the shell, as Avalonia bitmaps. Icons load on one background STA thread
/// (shell COM objects want STA) and are cached by file type; files that carry their own icon
/// (.exe, .lnk, .ico…) and customized folders are cached per path.
/// </summary>
public static class ShellIcons
{
    static readonly HashSet<string> PerFileExtensions = new(StringComparer.OrdinalIgnoreCase)
        { ".exe", ".lnk", ".url", ".ico", ".cur", ".ani", ".msc", ".scr", ".appref-ms", ".cpl" };

    const int MaxPerPathEntries = 3000;

    static readonly ConcurrentDictionary<string, Bitmap?> Cache = new(StringComparer.OrdinalIgnoreCase);
    static readonly Dictionary<string, List<FileItem>> Pending = new(StringComparer.OrdinalIgnoreCase);
    static readonly BlockingCollection<Action> Work = [];
    static int _perPathCount;
    static Thread? _worker;

    /// <summary>Pixel size for list icons: 16 DIPs at the screen's scaling. Set once at startup.</summary>
    public static int SmallSize { get; set; } = 16;

    /// <summary>
    /// Returns the cached icon for <paramref name="item"/>, or null and starts loading it; the item's
    /// <see cref="FileItem.Icon"/> updates when it arrives. Call on the UI thread.
    /// </summary>
    public static Bitmap? ForItem(FileItem item)
    {
        var key = KeyFor(item);
        if (Cache.TryGetValue(key, out var cached))
            return cached;

        lock (Pending)
        {
            if (Pending.TryGetValue(key, out var waiting))
            {
                waiting.Add(item);
                return null;
            }
            Pending[key] = [item];
        }
        _ = LoadForKeyAsync(key, item.FullPath);
        return null;
    }

    static async Task LoadForKeyAsync(string key, string path)
    {
        var bitmap = await LoadAsync(path, SmallSize);
        if (key.StartsWith("path:", StringComparison.Ordinal) && Interlocked.Increment(ref _perPathCount) > MaxPerPathEntries)
        {
            // Keep memory bounded when browsing folders full of programs or shortcuts.
            foreach (var k in Cache.Keys.Where(k => k.StartsWith("path:", StringComparison.Ordinal)))
                Cache.TryRemove(k, out _);
            Interlocked.Exchange(ref _perPathCount, 0);
        }
        Cache[key] = bitmap;

        List<FileItem> waiting;
        lock (Pending)
        {
            Pending.Remove(key, out waiting!);
        }
        if (bitmap is not null)
            Dispatcher.UIThread.Post(() => waiting.ForEach(i => i.SetIcon(bitmap)));
    }

    static string KeyFor(FileItem item)
    {
        if (item.IsDirectory)
        {
            // Folders with ReadOnly or System set may have a desktop.ini icon (Downloads, Music…).
            return item.Attributes.HasFlag(FileAttributes.ReadOnly) || item.Attributes.HasFlag(FileAttributes.System)
                ? "path:" + item.FullPath
                : "folder";
        }
        var ext = Path.GetExtension(item.Name);
        return PerFileExtensions.Contains(ext) ? "path:" + item.FullPath : "ext:" + ext;
    }

    /// <summary>Loads the shell icon for any path (file, folder, or drive) at <paramref name="pixelSize"/>.</summary>
    public static Task<Bitmap?> LoadAsync(string path, int pixelSize)
    {
        var tcs = new TaskCompletionSource<Bitmap?>(TaskCreationOptions.RunContinuationsAsynchronously);
        EnsureWorker();
        Work.Add(() =>
        {
            try
            {
                tcs.SetResult(Load(path, pixelSize));
            }
            catch (Exception e)
            {
                System.Diagnostics.Debug.WriteLine($"Icon for {path}: {e.Message}");
                tcs.SetResult(null);
            }
        });
        return tcs.Task;
    }

    static void EnsureWorker()
    {
        if (_worker is not null) return;
        lock (Work)
        {
            if (_worker is not null) return;
            var thread = new Thread(() =>
            {
                foreach (var job in Work.GetConsumingEnumerable())
                    job();
            })
            {
                IsBackground = true,
                Name = "Dockett shell icons",
            };
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            _worker = thread;
        }
    }

    static Bitmap? Load(string path, int pixelSize)
    {
        var iid = typeof(IShellItemImageFactory).GUID;
        if (SHCreateItemFromParsingName(path, 0, ref iid, out var factory) != 0 || factory is null)
            return null;
        try
        {
            if (factory.GetImage(new SIZE { cx = pixelSize, cy = pixelSize }, SIIGBF_ICONONLY, out var hbitmap) != 0 || hbitmap == 0)
                return null;
            try
            {
                return ToBitmap(hbitmap);
            }
            finally
            {
                DeleteObject(hbitmap);
            }
        }
        finally
        {
            Marshal.ReleaseComObject(factory);
        }
    }

    /// <summary>Copies a 32-bit HBITMAP into a premultiplied BGRA Avalonia bitmap.</summary>
    static Bitmap? ToBitmap(nint hbitmap)
    {
        if (GetObject(hbitmap, Marshal.SizeOf<BITMAP>(), out var info) == 0 || info.bmWidth <= 0 || info.bmHeight <= 0)
            return null;
        int width = info.bmWidth, height = info.bmHeight;

        var header = new BITMAPINFOHEADER
        {
            biSize = Marshal.SizeOf<BITMAPINFOHEADER>(),
            biWidth = width,
            biHeight = -height, // top-down rows
            biPlanes = 1,
            biBitCount = 32,
        };
        var pixels = new byte[width * height * 4];
        var dc = GetDC(0);
        try
        {
            if (GetDIBits(dc, hbitmap, 0, (uint)height, pixels, ref header, 0) == 0)
                return null;
        }
        finally
        {
            ReleaseDC(0, dc);
        }

        NormalizeAlpha(pixels);
        var pin = GCHandle.Alloc(pixels, GCHandleType.Pinned);
        try
        {
            return new Bitmap(PixelFormat.Bgra8888, AlphaFormat.Premul, pin.AddrOfPinnedObject(), new PixelSize(width, height), new Vector(96, 96), width * 4);
        }
        finally
        {
            pin.Free();
        }
    }

    /// <summary>
    /// Shell bitmaps come either premultiplied, with straight alpha, or (for old icons) with no alpha at all.
    /// Make them premultiplied so edges blend cleanly.
    /// </summary>
    static void NormalizeAlpha(byte[] bgra)
    {
        var anyAlpha = false;
        var straight = false;
        for (var i = 0; i < bgra.Length; i += 4)
        {
            var a = bgra[i + 3];
            if (a != 0) anyAlpha = true;
            if (bgra[i] > a || bgra[i + 1] > a || bgra[i + 2] > a) straight = true;
        }
        for (var i = 0; i < bgra.Length; i += 4)
        {
            if (!anyAlpha)
            {
                bgra[i + 3] = 255;
            }
            else if (straight)
            {
                var a = bgra[i + 3];
                bgra[i] = (byte)(bgra[i] * a / 255);
                bgra[i + 1] = (byte)(bgra[i + 1] * a / 255);
                bgra[i + 2] = (byte)(bgra[i + 2] * a / 255);
            }
        }
    }

    const int SIIGBF_ICONONLY = 0x4;

    [StructLayout(LayoutKind.Sequential)]
    struct SIZE
    {
        public int cx, cy;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct BITMAP
    {
        public int bmType, bmWidth, bmHeight, bmWidthBytes;
        public ushort bmPlanes, bmBitsPixel;
        public nint bmBits;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct BITMAPINFOHEADER
    {
        public int biSize, biWidth, biHeight;
        public ushort biPlanes, biBitCount;
        public uint biCompression, biSizeImage;
        public int biXPelsPerMeter, biYPelsPerMeter;
        public uint biClrUsed, biClrImportant;
    }

    [ComImport, Guid("BCC18B79-BA16-442F-80C4-8A59C30C463B"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IShellItemImageFactory
    {
        [PreserveSig] int GetImage(SIZE size, int flags, out nint phbm);
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    static extern int SHCreateItemFromParsingName(string path, nint pbc, ref Guid riid, out IShellItemImageFactory? ppv);

    [DllImport("gdi32.dll")] static extern int GetObject(nint h, int c, out BITMAP pv);
    [DllImport("gdi32.dll")] static extern int GetDIBits(nint hdc, nint hbm, uint start, uint lines, byte[] bits, ref BITMAPINFOHEADER bmi, uint usage);
    [DllImport("gdi32.dll")] static extern bool DeleteObject(nint ho);
    [DllImport("user32.dll")] static extern nint GetDC(nint hwnd);
    [DllImport("user32.dll")] static extern int ReleaseDC(nint hwnd, nint hdc);
}
