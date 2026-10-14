using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Dockett.Services;

namespace Dockett.Views;

public partial class SettingsWindow : Window
{
    const string RepositoryUrl = "https://github.com/hiramatx/dockett";

    public SettingsWindow()
    {
        InitializeComponent();
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape || (e.Key == Key.W && e.KeyModifiers == KeyModifiers.Control))
            {
                e.Handled = true;
                Close();
            }
        };
    }

    void OnOpenSettingsFolder(object? sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(JsonFile.Folder);
        Process.Start(new ProcessStartInfo("explorer.exe") { ArgumentList = { JsonFile.Folder } });
    }

    void OnOpenGitHub(object? sender, RoutedEventArgs e) =>
        Process.Start(new ProcessStartInfo(RepositoryUrl) { UseShellExecute = true });
}
