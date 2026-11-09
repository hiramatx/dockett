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

    /// <summary>
    /// Sends items to the Recycle Bin. Callers confirm with the user first, so the shell's own "are you sure"
    /// is skipped, except when an item can't be recycled (a network or removable drive, or too big for the
    /// bin): then Windows still asks before deleting it permanently.
    /// </summary>
    public static Task<bool> RecycleAsync(nint owner, IReadOnlyList<string> sources) =>
        RunAsync(owner, FO_DELETE, sources, null, FOF_NOCONFIRMATION | FOF_WANTNUKEWARNING);

    public static Task<bool> RenameAsync(nint owner, string source, string newName)
    {
        if (Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(source)) is not { Length: > 0 } folder)
            throw new ArgumentException("A drive can't be renamed here.", nameof(source));
        if (!IsValidFileName(newName))
            throw new ArgumentException("That name can't be used for a file or folder.", nameof(newName));
        return RunAsync(owner, FO_RENAME, [source], Path.Combine(folder, newName), 0);
    }

    /// <summary>True for a plain file or folder name: not empty, no path separators or other reserved characters, not "." or "..".</summary>
    public static bool IsValidFileName(string? name) =>
        !string.IsNullOrWhiteSpace(name)
        && name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0
        && name.IndexOfAny(['\\', '/', ':', '*', '?', '"', '<', '>', '|']) < 0
        && name.Trim() is not "." and not "..";

    public static void Open(string path) =>
        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });

    /// <summary>Shows Windows' "Open with" chooser for a file.</summary>
    public static void OpenWith(nint owner, string path) => ShellExecuteVerb(owner, path, "openas");

    /// <summary>Opens Windows Terminal in <paramref name="folder"/>, or PowerShell when Terminal isn't installed.</summary>
    public static void OpenTerminal(string folder)
    {
        try
        {
            // The folder is passed as the working directory, never on the command line: Windows Terminal treats
            // ";" as a command separator even inside quotes, so a folder named "x; calc" could otherwise run things.
            Process.Start(new ProcessStartInfo("wt.exe") { ArgumentList = { "-d", "." }, WorkingDirectory = folder, UseShellExecute = true });
        }
        catch (System.ComponentModel.Win32Exception)
        {
            Process.Start(new ProcessStartInfo("powershell.exe") { WorkingDirectory = folder, UseShellExecute = true });
        }
    }

    /// <summary>Opens Explorer with <paramref name="path"/> selected (or opens the folder itself when it's a drive root).</summary>
    public static void RevealInExplorer(string path)
    {
        var start = Path.GetPathRoot(path) == path
            ? new ProcessStartInfo("explorer.exe") { ArgumentList = { path } }
            : new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"");
        Process.Start(start);
    }

    public static void ShowProperties(nint owner, string path) => ShellExecuteVerb(owner, path, "properties");

    static void ShellExecuteVerb(nint owner, string path, string verb)
    {
        var info = new SHELLEXECUTEINFO
        {
            cbSize = Marshal.SizeOf<SHELLEXECUTEINFO>(),
            fMask = SEE_MASK_INVOKEIDLIST,
            hwnd = owner,
            lpVerb = verb,
            lpFile = path,
            nShow = 1,
        };
        ShellExecuteEx(ref info);
    }

    /// <summary>Zips <paramref name="sources"/> into <paramref name="zipPath"/>, keeping folder structure.</summary>
    /// <remarks>
    /// Hidden and system files are included. Links and junctions inside a folder are skipped, so a link that
    /// points back up the tree can't make the zip grow forever. A zip that fails part-way is removed.
    /// </remarks>
    public static Task CompressAsync(IReadOnlyList<string> sources, string zipPath) => Task.Run(() =>
    {
        // CreateNew: never replace a file that appeared after the caller checked the name was free.
        using (var stream = new FileStream(zipPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            try
            {
                using var zip = new ZipArchive(stream, ZipArchiveMode.Create);
                foreach (var source in sources)
                {
                    if (File.Exists(source))
                        zip.CreateEntryFromFile(source, Path.GetFileName(source), CompressionLevel.Optimal);
                    else
                        AddFolder(zip, source, Path.GetFileName(Path.TrimEndingDirectorySeparator(source)));
                }
                return;
            }
            catch
            {
                stream.Dispose();
                try
                {
                    File.Delete(zipPath);
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                }
                throw;
            }
        }
    });

    static void AddFolder(ZipArchive zip, string folder, string entryPrefix)
    {
        var options = new EnumerationOptions { IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint };
        var empty = true;
        foreach (var entry in new DirectoryInfo(folder).EnumerateFileSystemInfos("*", options))
        {
            empty = false;
            var name = $"{entryPrefix}/{entry.Name}";
            if (entry is DirectoryInfo)
                AddFolder(zip, entry.FullName, name);
            else
                zip.CreateEntryFromFile(entry.FullName, name, CompressionLevel.Optimal);
        }
        if (empty)
            zip.CreateEntry(entryPrefix + "/");
    }

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
        // The shell resolves relative names against the process's working directory and reads the list up to a
        // double null, so anything but complete paths could act on files the user never picked.
        if (sources.Count == 0 || !sources.All(IsCompletePath) || (destination is not null && !IsCompletePath(destination)))
            throw new ArgumentException("File operations need full paths.");

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
    const ushort FOF_RENAMEONCOLLISION = 0x8, FOF_NOCONFIRMATION = 0x10, FOF_ALLOWUNDO = 0x40, FOF_NOCONFIRMMKDIR = 0x200,
        FOF_WANTNUKEWARNING = 0x4000;

    /// <summary>A full path (drive or UNC share) with no embedded nulls, safe to hand to the shell.</summary>
    public static bool IsCompletePath(string? path) =>
        !string.IsNullOrWhiteSpace(path) && !path.Contains('\0') && Path.IsPathFullyQualified(path);
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
