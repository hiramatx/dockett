using System.Diagnostics;
using Dockett.Models;
using Dockett.Services;
using Dockett.ViewModels;

namespace Dockett.Tests;

public class SettingsTests
{
    sealed class NoPromptHost : IUiHost
    {
        public nint OwnerHandle => 0;
        public Task<string?> PromptAsync(string title, string message, string initialText, bool selectStem = false) => throw new InvalidOperationException("prompted");
        public Task<bool> ConfirmAsync(string title, string message, string confirmText, bool destructive = false) => throw new InvalidOperationException("asked to confirm");
        public Task AlertAsync(string title, string message) => throw new InvalidOperationException($"{title}: {message}");
        public Task ShowSettingsAsync(SettingsViewModel settings) => Task.CompletedTask;
    }

    static async Task<bool> WaitUntil(Func<bool> condition)
    {
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < 3000)
        {
            if (condition()) return true;
            await Task.Delay(25);
        }
        return condition();
    }

    [Fact]
    public void Missing_file_gives_defaults_and_values_round_trip()
    {
        using var dir = new TempDir();
        var file = Path.Combine(dir.Path, "settings.json");

        var defaults = SettingsStore.Load(file);
        Assert.Equal(new AppSettings(), defaults);
        Assert.True(defaults.ConfirmDelete);
        Assert.True(defaults.RestoreSession);

        var vm = new SettingsViewModel(defaults) { Theme = ThemeChoice.Dark, ShowHidden = true, ConfirmTransfers = false };
        SettingsStore.Save(vm.ToModel(), file);

        var loaded = SettingsStore.Load(file);
        Assert.Equal(ThemeChoice.Dark, loaded.Theme);
        Assert.True(loaded.ShowHidden);
        Assert.False(loaded.ConfirmTransfers);
        Assert.True(new SettingsViewModel(loaded).IsDarkTheme);
    }

    [Fact]
    public async Task Turning_on_hidden_files_shows_them()
    {
        using var dir = new TempDir();
        dir.File("visible.txt");
        var hidden = dir.File("secret.txt");
        File.SetAttributes(hidden, FileAttributes.Hidden);

        var vm = new MainViewModel([dir.Path]);
        await vm.InitializeAsync();
        var tab = vm.Left.SelectedTab;
        Assert.Equal(["visible.txt"], tab.Items.Select(i => i.Name));

        vm.Settings.ShowHidden = true;
        Assert.True(await WaitUntil(() => tab.Items.Count == 2));
        Assert.True(tab.Items.Single(i => i.Name == "secret.txt").IsHidden);

        vm.Settings.ShowHidden = false;
        Assert.True(await WaitUntil(() => tab.Items.Count == 1));
    }

    [Fact]
    public async Task Folders_first_can_be_turned_off()
    {
        using var dir = new TempDir();
        dir.File("a.txt");
        dir.Dir("b");
        dir.File("c.txt");

        var vm = new MainViewModel([dir.Path]);
        await vm.InitializeAsync();
        var tab = vm.Left.SelectedTab;
        Assert.Equal(["b", "a.txt", "c.txt"], tab.Items.Select(i => i.Name));

        vm.Settings.FoldersFirst = false;
        Assert.True(await WaitUntil(() => tab.Items[0].Name == "a.txt"));
        Assert.Equal(["a.txt", "b", "c.txt"], tab.Items.Select(i => i.Name));
    }

    [Fact]
    public async Task Copy_without_confirmation_when_turned_off()
    {
        using var dir = new TempDir();
        var src = dir.Dir("src");
        var dst = dir.Dir("dst");
        dir.File(@"src\doc.txt");

        var vm = new MainViewModel([src, dst], settings: new SettingsViewModel { ConfirmTransfers = false })
        {
            Host = new NoPromptHost(),
        };
        await vm.InitializeAsync();
        vm.Left.SelectedTab.CurrentItem = vm.Left.SelectedTab.Items.Single();

        await vm.CopyCommand.ExecuteAsync(null);

        Assert.True(File.Exists(Path.Combine(dst, "doc.txt")));
    }
}
