using Avalonia.Media.Imaging;
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
    public FileAttributes Attributes { get; init; }

    /// <summary>Updated in place by auto-refresh so rows keep their selection.</summary>
    [ObservableProperty, NotifyPropertyChangedFor(nameof(SizeText))]
    public partial long Size { get; set; }

    [ObservableProperty, NotifyPropertyChangedFor(nameof(ModifiedText))]
    public partial DateTime Modified { get; set; }
    public FileKind Kind { get; init; }

    /// <summary>Set by "Compare folders" when this item is missing from, or differs from, the other pane.</summary>
    [ObservableProperty]
    public partial bool IsDifferent { get; set; }

    /// <summary>True while this item is on the clipboard as a cut; shown faded.</summary>
    [ObservableProperty]
    public partial bool IsCut { get; set; }

    /// <summary>Hidden or system items, shown faded when "show hidden files" is on.</summary>
    public bool IsHidden => (Attributes & (FileAttributes.Hidden | FileAttributes.System)) != 0;

    public string SizeText => IsDirectory ? "" : Format.Size(Size);
    public string ModifiedText => Modified.ToString("yyyy-MM-dd  HH:mm");
    public string TypeText => IsDirectory ? "Folder" : FileTypes.Describe(Path.GetExtension(Name));

    /// <summary>Supplies real file icons; set by the app at startup. Unset (e.g. in tests) means shape icons only.</summary>
    public static Func<FileItem, Bitmap?>? IconSource { get; set; }

    Bitmap? _icon;
    bool _iconRequested;

    /// <summary>
    /// The Windows icon, loaded lazily the first time a visible row asks for it; null until it arrives,
    /// in which case the view shows a shape icon instead.
    /// </summary>
    public Bitmap? Icon
    {
        get
        {
            if (!_iconRequested && IconSource is { } source)
            {
                _iconRequested = true;
                _icon = source(this);
            }
            return _icon;
        }
    }

    public bool HasIcon => Icon is not null;

    public void SetIcon(Bitmap? icon)
    {
        _icon = icon;
        OnPropertyChanged(nameof(Icon));
        OnPropertyChanged(nameof(HasIcon));
    }

    // Used by the list's type-to-find.
    public override string ToString() => Name;

    public static FileItem FromInfo(FileSystemInfo info) => info switch
    {
        DirectoryInfo d => new FileItem
        {
            Name = d.Name,
            FullPath = d.FullName,
            IsDirectory = true,
            Attributes = d.Attributes,
            Modified = d.LastWriteTime,
            Kind = FileKind.Folder,
        },
        FileInfo f => new FileItem
        {
            Name = f.Name,
            FullPath = f.FullName,
            Attributes = f.Attributes,
            Size = f.Length,
            Modified = f.LastWriteTime,
            Kind = FileTypes.KindOf(f.Extension),
        },
        _ => throw new ArgumentException("Unsupported file system entry", nameof(info)),
    };
}
