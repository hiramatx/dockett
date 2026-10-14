using Dockett.Models;

namespace Dockett.Services;

public static class FileSystemService
{
    public static List<FileItem> List(string path, bool showHidden)
    {
        var options = new EnumerationOptions
        {
            IgnoreInaccessible = true,
            AttributesToSkip = showHidden ? 0 : FileAttributes.Hidden | FileAttributes.System,
        };

        // Touch the directory first so access errors on the folder itself surface instead of an empty list.
        var dir = new DirectoryInfo(path);
        using (var probe = Directory.EnumerateFileSystemEntries(path).GetEnumerator())
            probe.MoveNext();

        return dir.EnumerateFileSystemInfos("*", options).Select(FileItem.FromInfo).ToList();
    }

    /// <summary>Quick access folders (Home, Desktop…) and the current drives.</summary>
    public static IReadOnlyList<NavSection> BuildNavSections() => [BuildQuickAccess(), .. BuildDriveSections()];

    public static NavSection BuildQuickAccess()
    {
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var quick = new List<NavItem>
        {
            new() { Label = "Home", Path = profile, Icon = NavIcon.Home },
            new() { Label = "Desktop", Path = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), Icon = NavIcon.Desktop },
            new() { Label = "Documents", Path = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), Icon = NavIcon.Documents },
            new() { Label = "Downloads", Path = Path.Combine(profile, "Downloads"), Icon = NavIcon.Downloads },
            new() { Label = "Pictures", Path = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), Icon = NavIcon.Pictures },
        };
        quick.RemoveAll(i => string.IsNullOrEmpty(i.Path) || !Directory.Exists(i.Path));
        return new NavSection { Title = "Quick access", Items = quick };
    }

    /// <summary>"This PC" (local and removable drives) and, when there are any, "Network". Can be slow for network drives.</summary>
    public static IReadOnlyList<NavSection> BuildDriveSections()
    {
        var local = new List<NavItem>();
        var network = new List<NavItem>();
        foreach (var drive in DriveInfo.GetDrives())
        {
            NavItem item;
            try
            {
                if (!drive.IsReady) continue;
                var letter = drive.Name.TrimEnd('\\');
                var label = string.IsNullOrWhiteSpace(drive.VolumeLabel)
                    ? drive.DriveType == DriveType.Network ? "Network drive" : "Local disk"
                    : drive.VolumeLabel;
                item = new NavItem
                {
                    Label = $"{label} ({letter})",
                    Path = drive.RootDirectory.FullName,
                    Icon = drive.DriveType == DriveType.Network ? NavIcon.Network : NavIcon.Drive,
                    Usage = drive.TotalSize > 0 ? 1 - (double)drive.AvailableFreeSpace / drive.TotalSize : null,
                    Detail = $"{Format.Size(drive.AvailableFreeSpace)} free of {Format.Size(drive.TotalSize)}",
                };
            }
            catch (IOException)
            {
                continue;
            }
            catch (UnauthorizedAccessException)
            {
                continue;
            }
            (drive.DriveType == DriveType.Network ? network : local).Add(item);
        }

        var sections = new List<NavSection> { new() { Title = "This PC", Items = local } };
        if (network.Count > 0)
            sections.Add(new NavSection { Title = "Network", Items = network });
        return sections;
    }

    public static string? FreeSpaceText(string path)
    {
        try
        {
            var root = Path.GetPathRoot(path);
            if (string.IsNullOrEmpty(root)) return null;
            return $"{Format.Size(new DriveInfo(root).AvailableFreeSpace)} free";
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }

    /// <summary>Returns "name", "name (2)", "name (3)"… whichever doesn't exist yet in <paramref name="folder"/>.</summary>
    public static string UniqueName(string folder, string name)
    {
        var candidate = name;
        for (var n = 2; Path.Exists(Path.Combine(folder, candidate)); n++)
            candidate = $"{Path.GetFileNameWithoutExtension(name)} ({n}){Path.GetExtension(name)}";
        return candidate;
    }
}
