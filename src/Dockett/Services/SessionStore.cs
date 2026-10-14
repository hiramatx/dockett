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

    /// <summary>Name of the marker file that makes Dockett keep its data next to the exe.</summary>
    public const string PortableMarker = "Dockett.portable";

    public static string Folder { get; } = ChooseFolder(AppContext.BaseDirectory);

    /// <summary>True when settings live beside the exe (portable mode) rather than in %APPDATA%.</summary>
    public static bool IsPortable { get; } =
        string.Equals(Folder, Path.Combine(AppContext.BaseDirectory, "Data"), StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// <c>Data</c> beside the exe when a <see cref="PortableMarker"/> file is there and that folder is writable
    /// (e.g. on a USB stick); otherwise %APPDATA%\Dockett.
    /// </summary>
    public static string ChooseFolder(string exeFolder)
    {
        var appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Dockett");
        if (!File.Exists(Path.Combine(exeFolder, PortableMarker)))
            return appData;

        var portable = Path.Combine(exeFolder, "Data");
        try
        {
            Directory.CreateDirectory(portable);
            var probe = Path.Combine(portable, ".write-test");
            File.WriteAllText(probe, "");
            File.Delete(probe);
            return portable;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return appData; // read-only location, such as Program Files or a locked-down share
        }
    }

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
