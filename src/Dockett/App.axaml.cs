using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
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
            var session = SessionStore.Load();
            var vm = new MainViewModel(desktop.Args, session);
            var window = new MainWindow { DataContext = vm };
            window.ApplyPlacement(session?.Window);
            window.Closing += (_, _) => SessionStore.Save(window.CaptureSession());
            vm.Host = window;
            desktop.MainWindow = window;
            _ = vm.InitializeAsync();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
