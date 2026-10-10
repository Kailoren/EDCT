using System.IO;
using System.Text.Json;

namespace ColonyTracker.Services;

/// <summary>Shared JSON options + load/save helpers for Data\*.json files - previously each of the
/// seven services touching one of these files redeclared its own copy of the same options and
/// read/write boilerplate.</summary>
public static class JsonDataFile
{
    public static readonly JsonSerializerOptions ReadOptions = new() { PropertyNameCaseInsensitive = true };
    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };

    public static T LoadRequired<T>(string path) =>
        JsonSerializer.Deserialize<T>(File.ReadAllText(path), ReadOptions)
            ?? throw new InvalidDataException($"{Path.GetFileName(path)} deserialized to null.");

    public static IReadOnlyDictionary<string, TValue> LoadCaseInsensitiveMap<TValue>(string path)
    {
        var map = LoadRequired<Dictionary<string, TValue>>(path);
        return new Dictionary<string, TValue>(map, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Writes via a temp file + File.Replace/Move so a crash mid-write can't leave a
    /// truncated file at the real path - either the previous good file or nothing is there until
    /// the new content is fully written to disk.</summary>
    public static void SaveAtomic<T>(string path, T value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tempPath = path + ".tmp";
        File.WriteAllText(tempPath, JsonSerializer.Serialize(value, WriteOptions));
        if (File.Exists(path))
            File.Replace(tempPath, path, null);
        else
            File.Move(tempPath, path);
    }
}
