using System.Text;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using Dockett.Models;

namespace Dockett.ViewModels;

/// <summary>Shows the focused item of the active pane: an image, the first lines of a text file, or details.</summary>
public partial class PreviewViewModel : ViewModelBase
{
    const int MaxTextBytes = 32 * 1024;
    const int MaxTextLines = 200;

    CancellationTokenSource? _cts;

    [ObservableProperty]
    public partial FileItem? Item { get; private set; }

    [ObservableProperty]
    public partial string? Text { get; private set; }

    [ObservableProperty]
    public partial Bitmap? Image { get; private set; }

    [ObservableProperty]
    public partial string Details { get; private set; } = "";

    [ObservableProperty]
    public partial bool HasItem { get; private set; }

    public bool ShowText => Text is not null;
    public bool ShowImage => Image is not null;
    public bool ShowGlyph => HasItem && Text is null && Image is null;

    public async Task ShowAsync(FileItem? item)
    {
        _cts?.Cancel();
        var cts = _cts = new CancellationTokenSource();

        Item = item;
        HasItem = item is not null;
        SetContent(null, null);
        Details = item is null ? "" : BasicDetails(item);
        if (item is null) return;

        try
        {
            // Debounce so holding an arrow key doesn't decode every image on the way.
            await Task.Delay(120, cts.Token);
            var ext = Path.GetExtension(item.Name);

            if (item.IsDirectory)
            {
                var count = await Task.Run(() => CountChildren(item.FullPath), cts.Token);
                if (!cts.IsCancellationRequested && count is not null)
                    Details = $"Folder\n{Format.Count(count.Value, "item")}\nModified {item.ModifiedText}";
            }
            else if (FileTypes.IsPreviewableImage(ext))
            {
                var (bitmap, size) = await Task.Run(() => LoadImage(item.FullPath), cts.Token);
                if (cts.IsCancellationRequested)
                {
                    bitmap?.Dispose();
                    return;
                }
                SetContent(null, bitmap);
                if (size is not null)
                    Details = $"{item.TypeText}\n{item.SizeText} · {size}\nModified {item.ModifiedText}";
            }
            else if (FileTypes.IsPreviewableText(ext))
            {
                var text = await Task.Run(() => ReadText(item.FullPath), cts.Token);
                if (!cts.IsCancellationRequested)
                    SetContent(text, null);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            if (!cts.IsCancellationRequested)
                Details += "\nCan't read this file.";
        }
    }

    void SetContent(string? text, Bitmap? image)
    {
        var old = Image;
        Text = text;
        Image = image;
        if (old != image)
            old?.Dispose();
        OnPropertyChanged(nameof(ShowText));
        OnPropertyChanged(nameof(ShowImage));
        OnPropertyChanged(nameof(ShowGlyph));
    }

    static string BasicDetails(FileItem item) =>
        item.IsDirectory
            ? $"Folder\nModified {item.ModifiedText}"
            : $"{item.TypeText}\n{item.SizeText}\nModified {item.ModifiedText}";

    static int? CountChildren(string path)
    {
        try
        {
            return Directory.EnumerateFileSystemEntries(path, "*", new EnumerationOptions { IgnoreInaccessible = true }).Count();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    static (Bitmap?, string?) LoadImage(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            var full = new Bitmap(stream);
            var size = $"{full.PixelSize.Width}×{full.PixelSize.Height}";
            if (full.PixelSize.Width <= 600)
                return (full, size);
            var scaled = full.CreateScaledBitmap(new Avalonia.PixelSize(600, Math.Max(1, full.PixelSize.Height * 600 / full.PixelSize.Width)));
            full.Dispose();
            return (scaled, size);
        }
        catch (Exception e) when (e is not OperationCanceledException and not IOException and not UnauthorizedAccessException)
        {
            return (null, null); // unsupported or corrupt image
        }
    }

    static string ReadText(string path)
    {
        using var stream = File.OpenRead(path);
        var buffer = new byte[Math.Min(MaxTextBytes, stream.Length)];
        var read = stream.ReadAtLeast(buffer, buffer.Length, throwOnEndOfStream: false);
        if (buffer.AsSpan(0, read).Contains((byte)0))
            return "Binary file";
        var text = Encoding.UTF8.GetString(buffer, 0, read);
        var lines = text.Split('\n').Take(MaxTextLines);
        return string.Join('\n', lines).Replace("\r", "").Replace("\t", "    ");
    }
}
