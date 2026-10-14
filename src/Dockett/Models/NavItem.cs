using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Dockett.Models;

public enum NavIcon
{
    Home,
    Desktop,
    Documents,
    Downloads,
    Pictures,
    Drive,
    Network,
}

/// <summary>An entry in the navigation pane: a known folder or a drive.</summary>
public partial class NavItem : ObservableObject
{
    public required string Label { get; init; }
    public required string Path { get; init; }
    public NavIcon Icon { get; init; }

    /// <summary>0..1 used fraction for drives; null hides the usage bar.</summary>
    public double? Usage { get; init; }

    public string? Detail { get; init; }

    /// <summary>The Windows icon for the folder or drive, filled in after startup.</summary>
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasShellIcon))]
    public partial Bitmap? ShellIcon { get; set; }

    public bool HasShellIcon => ShellIcon is not null;

    public bool HasUsage => Usage is not null;
    public double UsagePercent => (Usage ?? 0) * 100;
}

public class NavSection
{
    public required string Title { get; init; }
    public required IReadOnlyList<NavItem> Items { get; init; }
}
