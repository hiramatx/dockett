namespace Dockett.Services;

public enum DropAction
{
    None,
    Copy,
    Move,
}

/// <summary>Decides what dropping files onto a folder does, following Explorer's rules.</summary>
public static class DropRules
{
    /// <summary>
    /// Ctrl copies and Shift moves. Otherwise items move within the same drive and copy across drives.
    /// Returns None when the drop would be a no-op or invalid (into the items' own folder, or into themselves).
    /// </summary>
    public static DropAction Choose(IReadOnlyList<string> sources, string targetFolder, bool ctrl, bool shift)
    {
        if (sources.Count == 0 || string.IsNullOrEmpty(targetFolder))
            return DropAction.None;

        var target = Normalize(targetFolder);
        foreach (var source in sources)
        {
            var path = Normalize(source);
            if (IsSameOrInside(target, path))
                return DropAction.None; // a folder into itself or its own subfolder
        }

        var allAlreadyThere = sources.All(s => string.Equals(Normalize(Path.GetDirectoryName(Normalize(s)) ?? ""), target, StringComparison.OrdinalIgnoreCase));
        if (ctrl && !shift)
            return DropAction.Copy; // copying into the same folder makes "- Copy" duplicates, like Explorer
        if (allAlreadyThere)
            return DropAction.None;
        if (shift && !ctrl)
            return DropAction.Move;

        var sameDrive = sources.All(s => string.Equals(Path.GetPathRoot(s), Path.GetPathRoot(targetFolder), StringComparison.OrdinalIgnoreCase));
        return sameDrive ? DropAction.Move : DropAction.Copy;
    }

    static string Normalize(string path) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

    static bool IsSameOrInside(string candidate, string folder) =>
        string.Equals(candidate, folder, StringComparison.OrdinalIgnoreCase)
        || candidate.StartsWith(folder + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
}
