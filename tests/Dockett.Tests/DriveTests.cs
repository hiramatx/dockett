using System.Diagnostics;
using Dockett.Models;
using Dockett.Services;
using Dockett.ViewModels;

namespace Dockett.Tests;

public class DriveTests
{
    static async Task<bool> WaitUntil(Func<bool> condition, int timeoutMs = 3000)
    {
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            if (condition()) return true;
            await Task.Delay(20);
        }
        return condition();
    }

    static NavSection Drives(params (string Path, string Label)[] drives) => new()
    {
        Title = "This PC",
        Items = drives.Select(d => new NavItem { Label = d.Label, Path = d.Path, Icon = NavIcon.Drive, Detail = "10 GB free" }).ToList(),
    };

    [Fact]
    public async Task Monitor_reports_added_or_removed_drives()
    {
        uint mask = 0b100; // C:
        var events = new List<bool>();
        using var monitor = new DriveMonitor(TimeSpan.FromMilliseconds(30), TimeSpan.FromHours(1), () => Volatile.Read(ref mask));
        monitor.Changed += e => { lock (events) events.Add(e); };

        await Task.Delay(150);
        lock (events) Assert.Empty(events); // nothing changed

        Volatile.Write(ref mask, 0b10100); // E: plugged in
        Assert.True(await WaitUntil(() => { lock (events) return events.Count == 1; }));
        lock (events) Assert.True(events[0]);
    }

    [Fact]
    public async Task Monitor_nudges_periodically_for_free_space()
    {
        var events = new List<bool>();
        using var monitor = new DriveMonitor(TimeSpan.FromMilliseconds(20), TimeSpan.FromMilliseconds(60), () => 0b100);
        monitor.Changed += e => { lock (events) events.Add(e); };

        Assert.True(await WaitUntil(() => { lock (events) return events.Count > 0; }));
        lock (events) Assert.False(events[0]);
    }

    [Fact]
    public void New_and_removed_drives_are_announced()
    {
        var vm = new MainViewModel();
        vm.ApplyDriveSections([Drives((@"C:\", "Local disk (C:)"))]); // known starting point, whatever this PC has
        vm.ApplyDriveSections([Drives((@"C:\", "Local disk (C:)"), (@"E:\", "USB (E:)"))]);
        Assert.Equal(1, vm.NavSections.Count(s => s.Title == "This PC"));
        Assert.Contains("USB (E:) connected", vm.StatusMessage);

        vm.ApplyDriveSections([Drives((@"C:\", "Local disk (C:)"))]);
        Assert.Equal("USB (E:) removed", vm.StatusMessage);
        Assert.DoesNotContain(vm.NavSections.SelectMany(s => s.Items), i => i.Path == @"E:\");
        Assert.Equal("Quick access", vm.NavSections[0].Title); // quick access is untouched
    }

    [Fact]
    public void Unchanged_drives_are_not_rebuilt()
    {
        var vm = new MainViewModel();
        vm.ApplyDriveSections([Drives((@"C:\", "Local disk (C:)"))]);
        var section = vm.NavSections[1];
        vm.StatusMessage = null;

        vm.ApplyDriveSections([Drives((@"C:\", "Local disk (C:)"))]);

        Assert.Same(section, vm.NavSections[1]);
        Assert.Null(vm.StatusMessage);
    }

    [Fact]
    public async Task Tab_on_a_vanished_drive_falls_back_to_home()
    {
        var tab = new TabViewModel(@"Q:\surely\not\a\drive");
        await tab.RefreshAsync();
        Assert.Equal(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), tab.CurrentPath);
        tab.Dispose();
    }
}
