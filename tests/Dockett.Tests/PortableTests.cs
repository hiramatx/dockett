using Dockett.Services;

namespace Dockett.Tests;

public class PortableTests
{
    static readonly string AppData =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Dockett");

    [Fact]
    public void Without_the_marker_data_goes_to_AppData()
    {
        using var dir = new TempDir();
        Assert.Equal(AppData, JsonFile.ChooseFolder(dir.Path));
        Assert.False(Directory.Exists(Path.Combine(dir.Path, "Data")));
    }

    [Fact]
    public void With_the_marker_data_goes_beside_the_exe()
    {
        using var dir = new TempDir();
        dir.File(JsonFile.PortableMarker, 0);

        var folder = JsonFile.ChooseFolder(dir.Path);

        Assert.Equal(Path.Combine(dir.Path, "Data"), folder);
        Assert.True(Directory.Exists(folder));
        Assert.Empty(Directory.GetFiles(folder)); // the write probe is cleaned up
    }
}
