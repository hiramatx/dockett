using System.Reflection;
using CommunityToolkit.Mvvm.ComponentModel;
using Dockett.Models;

namespace Dockett.ViewModels;

/// <summary>Live preferences shared by the whole app. Changes apply immediately; the app saves them.</summary>
public partial class SettingsViewModel : ViewModelBase
{
    public SettingsViewModel() : this(new AppSettings())
    {
    }

    public SettingsViewModel(AppSettings settings)
    {
        Theme = settings.Theme;
        ShowHidden = settings.ShowHidden;
        FoldersFirst = settings.FoldersFirst;
        ConfirmTransfers = settings.ConfirmTransfers;
        ConfirmDelete = settings.ConfirmDelete;
        RestoreSession = settings.RestoreSession;
    }

    [ObservableProperty, NotifyPropertyChangedFor(nameof(IsSystemTheme), nameof(IsLightTheme), nameof(IsDarkTheme))]
    public partial ThemeChoice Theme { get; set; }

    /// <summary>Show items marked hidden or system, faded like Explorer does.</summary>
    [ObservableProperty]
    public partial bool ShowHidden { get; set; }

    [ObservableProperty]
    public partial bool FoldersFirst { get; set; }

    /// <summary>Ask before copying or moving to the other pane (F5 and F6). Drag and drop never asks.</summary>
    [ObservableProperty]
    public partial bool ConfirmTransfers { get; set; }

    /// <summary>Ask before moving items to the Recycle Bin.</summary>
    [ObservableProperty]
    public partial bool ConfirmDelete { get; set; }

    [ObservableProperty]
    public partial bool RestoreSession { get; set; }

    // Radio buttons bind to these.
    public bool IsSystemTheme { get => Theme == ThemeChoice.System; set { if (value) Theme = ThemeChoice.System; } }
    public bool IsLightTheme { get => Theme == ThemeChoice.Light; set { if (value) Theme = ThemeChoice.Light; } }
    public bool IsDarkTheme { get => Theme == ThemeChoice.Dark; set { if (value) Theme = ThemeChoice.Dark; } }

    public string Version { get; } =
        Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3) ?? "0.1.0";

    public string DataLocation { get; } = Services.JsonFile.IsPortable
        ? "Portable mode: settings are kept in the Data folder next to Dockett.exe."
        : "Settings are kept in your AppData folder.";

    public AppSettings ToModel() => new()
    {
        Theme = Theme,
        ShowHidden = ShowHidden,
        FoldersFirst = FoldersFirst,
        ConfirmTransfers = ConfirmTransfers,
        ConfirmDelete = ConfirmDelete,
        RestoreSession = RestoreSession,
    };
}
