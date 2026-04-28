using System.Text.Encodings.Web;
using System.Text.Json;

namespace Ocr2Tran.App;

public sealed class ConfigStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    private bool _dirty;

    public ConfigStore(AppSettings settings, string path)
    {
        Settings = settings;
        Path = path;
        LastWriteTimeUtc = GetLastWriteTimeUtc(path);
    }

    public AppSettings Settings { get; private set; }
    public string Path { get; }
    public DateTime LastWriteTimeUtc { get; private set; }

    public static ConfigStore Load()
    {
        var path = System.IO.Path.Combine(AppContext.BaseDirectory, "appsettings.json");
        if (!File.Exists(path))
        {
            var defaults = new AppSettings();
            File.WriteAllText(path, JsonSerializer.Serialize(defaults, JsonOptions));
            return new ConfigStore(defaults, path);
        }

        var settings = ReadSettings(path);
        return new ConfigStore(settings, path);
    }

    public AppSettings Reload()
    {
        var settings = ReadSettings(Path);
        Settings = settings;
        _dirty = false;
        LastWriteTimeUtc = GetLastWriteTimeUtc(Path);
        return settings;
    }

    public bool HasChangedOnDiskSince(DateTime timestampUtc)
    {
        return GetLastWriteTimeUtc(Path) > timestampUtc;
    }

    public void MarkDirty()
    {
        _dirty = true;
    }

    public void ReplaceSettings(AppSettings settings)
    {
        Settings = settings;
        MarkDirty();
    }

    public void SaveIfDirty()
    {
        if (!_dirty)
        {
            return;
        }

        File.WriteAllText(Path, JsonSerializer.Serialize(Settings, JsonOptions));
        _dirty = false;
        LastWriteTimeUtc = GetLastWriteTimeUtc(Path);
    }

    private static AppSettings ReadSettings(string path)
    {
        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
    }

    private static DateTime GetLastWriteTimeUtc(string path)
    {
        return File.Exists(path) ? File.GetLastWriteTimeUtc(path) : DateTime.MinValue;
    }
}
