using Dockett.Services;
using Dockett.ViewModels;

namespace Dockett.Tests;

/// <summary>Skipped unless DOCKETT_CLIPBOARD_TESTS=1, because these replace whatever is on the clipboard.</summary>
public sealed class ClipboardFactAttribute : FactAttribute
{
    public ClipboardFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("DOCKETT_CLIPBOARD_TESTS") != "1")
            Skip = "Overwrites the clipboard. Set DOCKETT_CLIPBOARD_TESTS=1 to run.";
    }
}

public class ClipboardTests
{
    sealed class FakeHost : IUiHost
    {
        public nint OwnerHandle => 0;
        public Task<string?> PromptAsync(string title, string message, string initialText, bool selectStem = false) => Task.FromResult<string?>(initialText);
        public Task<bool> ConfirmAsync(string title, string message, string confirmText, bool destructive = false) => Task.FromResult(true);
        public Task AlertAsync(string title, string message) => throw new InvalidOperationException($"{title}: {message}");
    }

    [ClipboardFact]
    public void Files_round_trip_with_cut_flag()
    {
        using var dir = new TempDir();
        var a = dir.File("a.txt");
        var b = dir.File("b c.txt");

        WindowsClipboard.SetFiles([a, b], cut: false);
        var copied = WindowsClipboard.GetFiles();
        Assert.NotNull(copied);
        Assert.Equal([a, b], copied.Value.Paths);
        Assert.False(copied.Value.Cut);

        WindowsClipboard.SetFiles([a], cut: true);
        Assert.True(WindowsClipboard.GetFiles()?.Cut);

        WindowsClipboard.SetText("hello");
        Assert.False(WindowsClipboard.HasFiles);
        Assert.Null(WindowsClipboard.GetFiles());
    }

    [ClipboardFact]
    public async Task Cut_then_paste_moves_and_clears_the_clipboard()
    {
        using var dir = new TempDir();
        var file = dir.File(@"src\doc.txt");
        var target = dir.Dir("dst");

        var vm = new MainViewModel([Path.GetDirectoryName(file)!, target]) { Host = new FakeHost() };
        await vm.InitializeAsync();

        WindowsClipboard.SetFiles([file], cut: true);
        await vm.PasteCommand.ExecuteAsync(target);

        Assert.False(File.Exists(file));
        Assert.True(File.Exists(Path.Combine(target, "doc.txt")));
        Assert.False(WindowsClipboard.HasFiles);
    }

    [ClipboardFact]
    public async Task Copy_then_paste_into_same_folder_makes_a_copy()
    {
        using var dir = new TempDir();
        dir.File("doc.txt");
        var vm = new MainViewModel([dir.Path]) { Host = new FakeHost() };
        await vm.InitializeAsync();
        vm.Left.SelectedTab.CurrentItem = vm.Left.SelectedTab.Items.Single();

        await vm.CopyToClipboardCommand.ExecuteAsync(null);
        await vm.PasteCommand.ExecuteAsync(null);

        Assert.Equal(2, Directory.GetFiles(dir.Path).Length);
        Assert.True(WindowsClipboard.HasFiles); // a copy can be pasted again
    }
}
