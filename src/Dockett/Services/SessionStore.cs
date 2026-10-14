using System.Text.Json;
using System.Text.Json.Serialization;
using Dockett.Models;

namespace Dockett.Services;

/// <summary>Small JSON files in %APPDATA%\Dockett, read leniently and written atomically.</summary>
public static class JsonFile
{
    static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static string Folder { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Dockett");

    /// <summary>Returns null when the file is missing or can't be read; callers then use defaults.</summary>
    public static T? Load<T>(string path) where T : class
    {
        try
        {
            return File.Exists(path) ? JsonSerializer.Deserialize<T>(File.ReadAllText(path), Options) : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        {
            return null;
        }
    }

    /// <summary>Writes to a temp file, then replaces, so a crash mid-save can't leave a half-written file.</summary>
    public static void Save<T>(T value, string path)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var temp = path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(value, Options));
            File.Move(temp, path, overwrite: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Failing to save preferences shouldn't break the app.
        }
    }
}

/// <summary>Reads and writes <see cref="SessionState"/> (tabs and layout).</summary>
public static class SessionStore
{
    public static string DefaultPath { get; } = Path.Combine(JsonFile.Folder, "session.json");

    /// <summary>Returns null when there's no saved session or it can't be read; Dockett then starts fresh.</summary>
    public static SessionState? Load(string? path = null) => JsonFile.Load<SessionState>(path ?? DefaultPath);

    public static void Save(SessionState state, string? path = null) => JsonFile.Save(state, path ?? DefaultPath);

    /// <summary>The folder itself if it exists, else its nearest existing parent, else null (e.g. the drive is gone).</summary>
    public static string? NearestExisting(string path)
    {
        try
        {
            for (var dir = new DirectoryInfo(path); dir is not null; dir = dir.Parent)
            {
                if (dir.Exists)
                    return dir.FullName;
            }
        }
        catch (Exception e) when (e is ArgumentException or IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
        }
        return null;
    }
}

/// <summary>Reads and writes <see cref="AppSettings"/>.</summary>
public static class SettingsStore
{
    public static string DefaultPath { get; } = Path.Combine(JsonFile.Folder, "settings.json");

    public static AppSettings Load(string? path = null) => JsonFile.Load<AppSettings>(path ?? DefaultPath) ?? new AppSettings();

    public static void Save(AppSettings settings, string? path = null) => JsonFile.Save(settings, path ?? DefaultPath);
}
