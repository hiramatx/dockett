using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using Avalonia.Threading;
using Dockett.Models;
using Dockett.Services;
using Dockett.ViewModels;
using Dockett.Views;

namespace Dockett;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var settings = new SettingsViewModel(SettingsStore.Load());
            ApplyTheme(settings.Theme);
            settings.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(SettingsViewModel.Theme))
                    ApplyTheme(settings.Theme);
                SettingsStore.Save(settings.ToModel());
            };

            var session = SessionStore.Load();
            if (!settings.RestoreSession)
                session = null;
            var vm = new MainViewModel(desktop.Args, session, settings);
            var window = new MainWindow { DataContext = vm };
            UseShellIcons(window, vm);
            window.ApplyPlacement(session?.Window);
            window.Closing += (_, _) => SessionStore.Save(window.CaptureSession());

            // Pick up drives as they're plugged in or removed, and keep free space current.
            var drives = new DriveMonitor();
            drives.Changed += drivesAddedOrRemoved => Dispatcher.UIThread.Post(() => _ = vm.RefreshDrivesAsync());
            desktop.Exit += (_, _) => drives.Dispose();
            vm.Host = window;
            desktop.MainWindow = window;
            _ = vm.InitializeAsync();
        }

        base.OnFrameworkInitializationCompleted();
    }

    void ApplyTheme(ThemeChoice theme) => RequestedThemeVariant = theme switch
    {
        ThemeChoice.Light => ThemeVariant.Light,
        ThemeChoice.Dark => ThemeVariant.Dark,
        _ => ThemeVariant.Default,
    };

    /// <summary>Turns on real Windows icons, sized for the primary screen's scaling.</summary>
    static void UseShellIcons(MainWindow window, MainViewModel vm)
    {
        var scaling = window.Screens.Primary?.Scaling ?? 1;
        ShellIcons.SmallSize = (int)Math.Round(16 * scaling);
        var large = (int)Math.Round(64 * scaling);

        FileItem.IconSource = ShellIcons.ForItem;
        PreviewViewModel.LargeIconSource = path => ShellIcons.LoadAsync(path, large);

        MainViewModel.NavIconSource = path => ShellIcons.LoadAsync(path, ShellIcons.SmallSize);
        vm.LoadNavIcons();
    }
}
