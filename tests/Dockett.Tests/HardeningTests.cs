using System.IO.Compression;
using Dockett.Models;
using Dockett.Services;
using Dockett.ViewModels;

namespace Dockett.Tests;

/// <summary>Bad input, damaged files, and failures part-way through must end in a message, never a crash or a stray file.</summary>
public class HardeningTests
{
    sealed class ScriptedHost(string? promptAnswer) : IUiHost
    {
        public List<string> Alerts { get; } = [];
        public nint OwnerHandle => 0;
        public Task<string?> PromptAsync(string title, string message, string initialText, bool selectStem = false) => Task.FromResult(promptAnswer);
        public Task<bool> ConfirmAsync(string title, string message, string confirmText, bool destructive = false) => Task.FromResult(true);
        public Task AlertAsync(string title, string message)
        {
            Alerts.Add(title);
            return Task.CompletedTask;
        }
        public Task ShowSettingsAsync(SettingsViewModel settings) => Task.CompletedTask;
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\"\"")]
    [InlineData("bad\0path")]
    public async Task Nonsense_in_the_address_bar_shows_an_error_instead_of_throwing(string typed)
    {
        using var dir = new TempDir();
        var tab = new TabViewModel(dir.Path);
        await tab.InitializeAsync();

        Assert.False(await tab.NavigateAsync(typed));

        Assert.NotNull(tab.ErrorText);
        Assert.Equal(dir.Path, tab.CurrentPath);
        Assert.False(tab.IsLoading);
    }

    [Fact]
    public async Task Relative_path_is_taken_from_the_current_folder()
    {
        using var dir = new TempDir();
        var sub = dir.Dir("sub");
        var tab = new TabViewModel(dir.Path);
        await tab.InitializeAsync();

        Assert.True(await tab.NavigateAsync("sub"));

        Assert.Equal(sub, tab.CurrentPath);
    }

    [Theory]
    [InlineData("notes.txt", true)]
    [InlineData("New folder (2)", true)]
    [InlineData("", false)]
    [InlineData("  ", false)]
    [InlineData(".", false)]
    [InlineData("..", false)]
    [InlineData(@"..\up.txt", false)]
    [InlineData("a/b", false)]
    [InlineData(@"C:\x.zip", false)]
    [InlineData("what?.txt", false)]
    [InlineData("pipe|name", false)]
    public void File_names_are_checked_before_use(string name, bool valid) =>
        Assert.Equal(valid, ShellOperations.IsValidFileName(name));

    [Fact]
    public async Task Shell_operations_refuse_paths_that_are_not_complete()
    {
        using var dir = new TempDir();
        var file = dir.File("a.txt");

        await Assert.ThrowsAsync<ArgumentException>(() => ShellOperations.CopyAsync(0, ["a.txt"], dir.Path));
        await Assert.ThrowsAsync<ArgumentException>(() => ShellOperations.MoveAsync(0, [file], "somewhere"));
        await Assert.ThrowsAsync<ArgumentException>(() => ShellOperations.RecycleAsync(0, []));
        await Assert.ThrowsAsync<ArgumentException>(() => ShellOperations.RenameAsync(0, file, @"..\escaped.txt"));
        Assert.True(File.Exists(file));
    }

    [Fact]
    public async Task Compress_includes_hidden_files_and_empty_folders()
    {
        using var dir = new TempDir();
        var folder = dir.Dir("proj");
        dir.File(Path.Combine("proj", "a.txt"));
        var hidden = dir.File(Path.Combine("proj", "secret.txt"));
        if (OperatingSystem.IsWindows())
            File.SetAttributes(hidden, FileAttributes.Hidden);
        dir.Dir(Path.Combine("proj", "sub", "empty"));
        var zip = Path.Combine(dir.Path, "out.zip");

        await ShellOperations.CompressAsync([folder], zip);

        using var archive = ZipFile.OpenRead(zip);
        Assert.Equal(["proj/a.txt", "proj/secret.txt", "proj/sub/empty/"], archive.Entries.Select(e => e.FullName).Order());
    }

    [Fact]
    public async Task Compress_that_fails_leaves_no_half_written_zip()
    {
        using var dir = new TempDir();
        var good = dir.File("good.txt");
        var zip = Path.Combine(dir.Path, "out.zip");

        await Assert.ThrowsAnyAsync<IOException>(() =>
            ShellOperations.CompressAsync([good, Path.Combine(dir.Path, "gone")], zip));

        Assert.False(File.Exists(zip));
    }

    [Fact]
    public async Task Compress_never_replaces_an_existing_file()
    {
        using var dir = new TempDir();
        var source = dir.File("a.txt");
        var existing = dir.File("out.zip", 7);

        await Assert.ThrowsAnyAsync<IOException>(() => ShellOperations.CompressAsync([source], existing));

        Assert.Equal(7, new FileInfo(existing).Length);
    }

    [Theory]
    [InlineData(@"..\escaped")]
    [InlineData("sub/inner")]
    public async Task Compress_rejects_a_name_that_would_leave_the_folder(string typed)
    {
        using var outer = new TempDir();
        var inner = outer.Dir("inner");
        Directory.CreateDirectory(Path.Combine(inner, "sub"));
        File.WriteAllText(Path.Combine(inner, "a.txt"), "x");
        var host = new ScriptedHost(typed);
        var vm = new MainViewModel([inner]) { Host = host };
        await vm.InitializeAsync();
        vm.ActiveTab.CurrentItem = vm.ActiveTab.Items.First(i => i.Name == "a.txt");

        await vm.CompressCommand.ExecuteAsync(null);

        Assert.Equal(["Can't use that name"], host.Alerts);
        Assert.Empty(Directory.GetFiles(outer.Path, "*.zip", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task Compare_copes_with_names_that_differ_only_by_case()
    {
        using var left = new TempDir();
        using var right = new TempDir();
        // Two files on a case-sensitive folder; one on a normal Windows folder. Either way, no crash.
        left.File("Readme.txt");
        left.File("README.TXT");
        right.File("readme.txt");

        var vm = new MainViewModel();
        await vm.Left.SelectedTab.NavigateAsync(left.Path);
        await vm.Right.SelectedTab.NavigateAsync(right.Path);
        vm.CompareCommand.Execute(null);

        Assert.NotNull(vm.StatusMessage);
    }

    [Theory]
    [InlineData("""{"Left":{"Tabs":null},"Right":{"Tabs":null,"SelectedIndex":5}}""")]
    [InlineData("""{"Left":{"Tabs":[null,{"Path":""},{"Path":"   "}],"SelectedIndex":-3}}""")]
    [InlineData("""{"Left":{"Tabs":[{"Path":"??<>|\u0000"}]},"Window":{"Width":1e300,"Height":-1,"LeftShare":1e300}}""")]
    public void Damaged_session_still_starts(string json)
    {
        using var dir = new TempDir();
        var file = Path.Combine(dir.Path, "session.json");
        File.WriteAllText(file, json);

        var vm = new MainViewModel(session: SessionStore.Load(file));

        Assert.Single(vm.Left.Tabs);
        Assert.Single(vm.Right.Tabs);
    }

    [Fact]
    public void Session_with_thousands_of_tabs_opens_a_bounded_number()
    {
        using var dir = new TempDir();
        var tabs = Enumerable.Repeat(new TabState { Path = dir.Path }, 5000).ToList();

        var vm = new MainViewModel(session: new SessionState { Left = new PaneState { Tabs = tabs, SelectedIndex = 4999 } });

        Assert.InRange(vm.Left.Tabs.Count, 1, 50);
    }

    [Fact]
    public void Oversized_settings_file_is_ignored()
    {
        using var dir = new TempDir();
        var file = Path.Combine(dir.Path, "settings.json");
        File.WriteAllText(file, "{\"Theme\":\"Dark\",\"pad\":\"" + new string('x', 5 * 1024 * 1024) + "\"}");

        Assert.Equal(new AppSettings(), SettingsStore.Load(file));
    }

    [Fact]
    public void Crash_log_records_the_error_and_never_throws()
    {
        using var dir = new TempDir();
        var file = Path.Combine(dir.Path, "nested", "crash.log");

        CrashLog.Write(new InvalidOperationException("boom"), file);
        CrashLog.Write(new InvalidOperationException("again"), file);
        CrashLog.Write("unwritable", Path.Combine(file, "under-a-file", "crash.log"));

        var text = File.ReadAllText(file);
        Assert.Contains("boom", text);
        Assert.Contains("again", text);
    }

    [Fact]
    public void Drops_with_incomplete_paths_do_nothing()
    {
        using var dir = new TempDir();

        Assert.Equal(DropAction.None, DropRules.Choose(["file.txt"], dir.Path, ctrl: false, shift: false));
        Assert.Equal(DropAction.None, DropRules.Choose([Path.Combine(dir.Path, "a.txt")], "relative", ctrl: true, shift: false));
        Assert.Equal(DropAction.None, DropRules.Choose(["bad\0name"], dir.Path, ctrl: false, shift: false));
    }
}
