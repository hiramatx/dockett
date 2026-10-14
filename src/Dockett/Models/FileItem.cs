using CommunityToolkit.Mvvm.ComponentModel;

namespace Dockett.Models;

public enum FileKind
{
    Folder,
    File,
    Image,
    Text,
    Code,
    Archive,
    Document,
}

/// <summary>One row in a file pane.</summary>
public partial class FileItem : ObservableObject
{
    public required string Name { get; init; }
    public required string FullPath { get; init; }
    public bool IsDirectory { get; init; }
    public long Size { get; init; }
    public DateTime Modified { get; init; }
    public FileKind Kind { get; init; }

    /// <summary>Set by "Compare folders" when this item is missing from, or differs from, the other pane.</summary>
    [ObservableProperty]
    public partial bool IsDifferent { get; set; }

    public string SizeText => IsDirectory ? "" : Format.Size(Size);
    public string ModifiedText => Modified.ToString("yyyy-MM-dd  HH:mm");
    public string TypeText => IsDirectory ? "Folder" : FileTypes.Describe(Path.GetExtension(Name));

    // Used by the list's type-to-find.
    public override string ToString() => Name;

    public static FileItem FromInfo(FileSystemInfo info) => info switch
    {
        DirectoryInfo d => new FileItem
        {
            Name = d.Name,
            FullPath = d.FullName,
            IsDirectory = true,
            Modified = d.LastWriteTime,
            Kind = FileKind.Folder,
        },
        FileInfo f => new FileItem
        {
            Name = f.Name,
            FullPath = f.FullName,
            Size = f.Length,
            Modified = f.LastWriteTime,
            Kind = FileTypes.KindOf(f.Extension),
        },
        _ => throw new ArgumentException("Unsupported file system entry", nameof(info)),
    };
}
