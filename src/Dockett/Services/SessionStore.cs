using System.Text.Json;
using System.Text.Json.Serialization;
using Dockett.Models;

namespace Dockett.Services;

/// <summary>Reads and writes <see cref="SessionState"/> as JSON in %APPDATA%\Dockett.</summary>
public static class SessionStore
{
    static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static string DefaultPath { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Dockett", "session.json");

    /// <summary>Returns null when there's no saved session or it can't be read; Dockett then starts fresh.</summary>
    public static SessionState? Load(string? path = null)
    {
        path ??= DefaultPath;
        try
        {
            return File.Exists(path) ? JsonSerializer.Deserialize<SessionState>(File.ReadAllText(path), Options) : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        {
            return null;
        }
    }

    /// <summary>Writes atomically (temp file, then replace) so a crash mid-save can't leave a half-written file.</summary>
    public static void Save(SessionState state, string? path = null)
    {
        path ??= DefaultPath;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var temp = path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(state, Options));
            File.Move(temp, path, overwrite: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Not being able to save the session shouldn't stop the app from closing.
        }
    }

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
