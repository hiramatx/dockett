using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
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

        foreach (var item in vm.NavSections.SelectMany(s => s.Items))
            _ = LoadNavIconAsync(item);

        static async Task LoadNavIconAsync(NavItem item) =>
            item.ShellIcon = await ShellIcons.LoadAsync(item.Path, ShellIcons.SmallSize);
    }
}
