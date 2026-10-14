using System.ComponentModel;
using Dockett.Models;

namespace Dockett.Tests;

public class IconTests
{
    [Fact]
    public void Icon_is_requested_once_and_only_when_read()
    {
        var item = new FileItem { Name = "a.txt", FullPath = @"C:\a.txt" };
        var requests = 0;
        var previous = FileItem.IconSource;
        FileItem.IconSource = _ =>
        {
            requests++;
            return null; // still loading
        };
        try
        {
            Assert.Equal(0, requests); // creating rows doesn't load icons
            Assert.Null(item.Icon);
            Assert.False(item.HasIcon);
            _ = item.Icon;
            Assert.Equal(1, requests);

            var changed = new List<string?>();
            ((INotifyPropertyChanged)item).PropertyChanged += (_, e) => changed.Add(e.PropertyName);
            item.SetIcon(null);
            Assert.Contains(nameof(FileItem.Icon), changed);
            Assert.Contains(nameof(FileItem.HasIcon), changed);
        }
        finally
        {
            FileItem.IconSource = previous;
        }
    }

    [Fact]
    public void Without_an_icon_source_rows_use_shape_icons()
    {
        var item = new FileItem { Name = "a.txt", FullPath = @"C:\a.txt" };
        Assert.Null(item.Icon);
        Assert.False(item.HasIcon);
    }
}
