using System.Diagnostics;
using System.IO.Compression;
using System.Runtime.InteropServices;

namespace Dockett.Services;

/// <summary>
/// File operations that go through the Windows shell, so users get the native progress,
/// conflict, and error dialogs plus Undo support in Explorer.
/// </summary>
public static class ShellOperations
{
    public static Task<bool> CopyAsync(nint owner, IReadOnlyList<string> sources, string destination) =>
        RunAsync(owner, FO_COPY, sources, destination, CollisionFlags(sources, destination));

    public static Task<bool> MoveAsync(nint owner, IReadOnlyList<string> sources, string destination) =>
        RunAsync(owner, FO_MOVE, sources, destination, CollisionFlags(sources, destination));

    /// <summary>Sends items to the Recycle Bin. Callers confirm with the user first.</summary>
    public static Task<bool> RecycleAsync(nint owner, IReadOnlyList<string> sources) =>
        RunAsync(owner, FO_DELETE, sources, null, FOF_NOCONFIRMATION);

    public static Task<bool> RenameAsync(nint owner, string source, string newName) =>
        RunAsync(owner, FO_RENAME, [source], Path.Combine(Path.GetDirectoryName(source)!, newName), 0);

    public static void Open(string path) =>
        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });

    public static void ShowProperties(nint owner, string path)
    {
        var info = new SHELLEXECUTEINFO
        {
            cbSize = Marshal.SizeOf<SHELLEXECUTEINFO>(),
            fMask = SEE_MASK_INVOKEIDLIST,
            hwnd = owner,
            lpVerb = "properties",
            lpFile = path,
            nShow = 1,
        };
        ShellExecuteEx(ref info);
    }

    /// <summary>Zips <paramref name="sources"/> into <paramref name="zipPath"/>, keeping folder structure.</summary>
    public static Task CompressAsync(IReadOnlyList<string> sources, string zipPath) => Task.Run(() =>
    {
        using var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create);
        foreach (var source in sources)
        {
            if (File.Exists(source))
            {
                zip.CreateEntryFromFile(source, Path.GetFileName(source), CompressionLevel.Optimal);
                continue;
            }

            var parent = Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(source))!;
            var files = Directory.EnumerateFiles(source, "*", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true });
            var any = false;
            foreach (var file in files)
            {
                zip.CreateEntryFromFile(file, Path.GetRelativePath(parent, file).Replace('\\', '/'), CompressionLevel.Optimal);
                any = true;
            }
            if (!any)
                zip.CreateEntry(Path.GetRelativePath(parent, source).Replace('\\', '/') + "/");
        }
    });

    // Copying or moving into the folder the items already live in makes Windows add " - Copy".
    static ushort CollisionFlags(IReadOnlyList<string> sources, string destination) =>
        sources.Any(s => string.Equals(Path.GetDirectoryName(s), Path.TrimEndingDirectorySeparator(destination), StringComparison.OrdinalIgnoreCase))
            ? FOF_RENAMEONCOLLISION
            : (ushort)0;

    /// <summary>
    /// SHFileOperation shows modal shell UI and wants an STA thread, so each call gets its own thread
    /// instead of blocking the Avalonia UI thread.
    /// </summary>
    static Task<bool> RunAsync(nint owner, uint func, IReadOnlyList<string> sources, string? destination, ushort extraFlags)
    {
        var tcs = new TaskCompletionSource<bool>();
        var thread = new Thread(() =>
        {
            try
            {
                var op = new SHFILEOPSTRUCT
                {
                    hwnd = owner,
                    wFunc = func,
                    pFrom = string.Join('\0', sources) + "\0\0",
                    pTo = destination is null ? null : destination + "\0\0",
                    fFlags = (ushort)(FOF_ALLOWUNDO | FOF_NOCONFIRMMKDIR | extraFlags),
                };
                var result = SHFileOperation(ref op);
                tcs.SetResult(result == 0 && !op.fAnyOperationsAborted);
            }
            catch (Exception e)
            {
                tcs.SetException(e);
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();
        return tcs.Task;
    }

    const uint FO_MOVE = 1, FO_COPY = 2, FO_DELETE = 3, FO_RENAME = 4;
    const ushort FOF_RENAMEONCOLLISION = 0x8, FOF_NOCONFIRMATION = 0x10, FOF_ALLOWUNDO = 0x40, FOF_NOCONFIRMMKDIR = 0x200;
    const uint SEE_MASK_INVOKEIDLIST = 0xC;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct SHFILEOPSTRUCT
    {
        public nint hwnd;
        public uint wFunc;
        public string pFrom;
        public string? pTo;
        public ushort fFlags;
        [MarshalAs(UnmanagedType.Bool)] public bool fAnyOperationsAborted;
        public nint hNameMappings;
        public string? lpszProgressTitle;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct SHELLEXECUTEINFO
    {
        public int cbSize;
        public uint fMask;
        public nint hwnd;
        public string? lpVerb;
        public string? lpFile;
        public string? lpParameters;
        public string? lpDirectory;
        public int nShow;
        public nint hInstApp;
        public nint lpIDList;
        public string? lpClass;
        public nint hkeyClass;
        public uint dwHotKey;
        public nint hIcon;
        public nint hProcess;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    static extern int SHFileOperation(ref SHFILEOPSTRUCT lpFileOp);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool ShellExecuteEx(ref SHELLEXECUTEINFO lpExecInfo);
}
