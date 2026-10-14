namespace Dockett.Models;

public static class FileTypes
{
    static readonly HashSet<string> Images = new(StringComparer.OrdinalIgnoreCase)
        { ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".webp", ".ico" };

    static readonly HashSet<string> Texts = new(StringComparer.OrdinalIgnoreCase)
        { ".txt", ".md", ".log", ".csv", ".ini", ".cfg", ".conf", ".gitignore", ".editorconfig" };

    static readonly HashSet<string> Code = new(StringComparer.OrdinalIgnoreCase)
    {
        ".cs", ".axaml", ".xaml", ".csproj", ".sln", ".slnx", ".props", ".targets", ".json", ".xml", ".yml",
        ".yaml", ".toml", ".ps1", ".psm1", ".bat", ".cmd", ".sh", ".py", ".js", ".ts", ".tsx", ".jsx",
        ".html", ".htm", ".css", ".scss", ".sql", ".c", ".h", ".cpp", ".hpp", ".go", ".rs", ".java", ".kt",
    };

    static readonly HashSet<string> Archives = new(StringComparer.OrdinalIgnoreCase)
        { ".zip", ".7z", ".rar", ".tar", ".gz", ".tgz", ".bz2", ".xz" };

    static readonly HashSet<string> Documents = new(StringComparer.OrdinalIgnoreCase)
        { ".pdf", ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx", ".odt", ".ods", ".rtf" };

    public static FileKind KindOf(string extension) =>
        Images.Contains(extension) ? FileKind.Image
        : Code.Contains(extension) ? FileKind.Code
        : Texts.Contains(extension) ? FileKind.Text
        : Archives.Contains(extension) ? FileKind.Archive
        : Documents.Contains(extension) ? FileKind.Document
        : FileKind.File;

    /// <summary>True when the preview panel should show the file's first lines as text.</summary>
    public static bool IsPreviewableText(string extension) => Texts.Contains(extension) || Code.Contains(extension);

    public static bool IsPreviewableImage(string extension) => Images.Contains(extension);

    public static string Describe(string extension) =>
        string.IsNullOrEmpty(extension) ? "File" : $"{extension.TrimStart('.').ToUpperInvariant()} file";
}
