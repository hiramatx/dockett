using Avalonia;
using System;
using System.IO;
using Dockett.Services;

namespace Dockett;

sealed class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        // Leave a trace when something unexpected ends the app, so a crash can be reported and fixed.
        AppDomain.CurrentDomain.UnhandledException += (_, e) => CrashLog.Write(e.ExceptionObject);
        // A forgotten background task that failed is not a reason to stop.
        TaskScheduler.UnobservedTaskException += (_, e) => e.SetObserved();

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
#if DEBUG
            .WithDeveloperTools()
#endif
            .WithInterFont()
            .LogToTrace();
}

/// <summary>Appends unexpected failures to <c>crash.log</c> next to the settings. Never throws.</summary>
public static class CrashLog
{
    const long MaxBytes = 256 * 1024;

    public static string DefaultPath { get; } = Path.Combine(JsonFile.Folder, "crash.log");

    public static void Write(object? error, string? path = null)
    {
        try
        {
            path ??= DefaultPath;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            // Start over rather than grow without limit if something crashes repeatedly.
            if (File.Exists(path) && new FileInfo(path).Length > MaxBytes)
                File.Delete(path);
            var version = typeof(CrashLog).Assembly.GetName().Version;
            File.AppendAllText(path, $"[{DateTime.UtcNow:u}] Dockett {version} on {Environment.OSVersion}{Environment.NewLine}{error}{Environment.NewLine}{Environment.NewLine}");
        }
        catch
        {
            // Nothing sensible left to do.
        }
    }
}
