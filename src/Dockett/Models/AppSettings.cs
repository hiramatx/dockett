namespace Dockett.Models;

public enum ThemeChoice
{
    System,
    Light,
    Dark,
}

/// <summary>User preferences, saved as JSON. Missing fields fall back to these defaults.</summary>
public sealed record AppSettings
{
    public int Version { get; init; } = 1;
    public ThemeChoice Theme { get; init; } = ThemeChoice.System;
    public bool ShowHidden { get; init; }
    public bool FoldersFirst { get; init; } = true;
    public bool ConfirmTransfers { get; init; } = true;
    public bool ConfirmDelete { get; init; } = true;
    public bool RestoreSession { get; init; } = true;
}
