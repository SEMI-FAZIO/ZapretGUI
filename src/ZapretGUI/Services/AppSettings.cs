using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ZapretGUI.Services;

public enum AppTheme { Dark, Light, System }
public enum AppLanguage { Ru, En, System }

public sealed class AppSettings
{
    [JsonPropertyName("zapretRoot")]
    public string? ZapretRoot { get; set; }

    [JsonPropertyName("theme")]
    public AppTheme Theme { get; set; } = AppTheme.Dark;

    [JsonPropertyName("language")]
    public AppLanguage Language { get; set; } = AppLanguage.System;

    [JsonPropertyName("lastStrategy")]
    public string? LastStrategy { get; set; }

    [JsonPropertyName("startWithWindows")]
    public bool StartWithWindows { get; set; }

    [JsonPropertyName("startMinimized")]
    public bool StartMinimized { get; set; }

    [JsonPropertyName("autoStartBypass")]
    public bool AutoStartBypass { get; set; }

    [JsonPropertyName("hideOnClose")]
    public bool HideOnClose { get; set; }

    [JsonPropertyName("firstRunCompleted")]
    public bool FirstRunCompleted { get; set; }

    [JsonPropertyName("captureWinwsLogs")]
    public bool CaptureWinwsLogs { get; set; } = true;

    [JsonPropertyName("windowLeft")]
    public double WindowLeft { get; set; } = double.MinValue;

    [JsonPropertyName("windowTop")]
    public double WindowTop { get; set; } = double.MinValue;

    [JsonPropertyName("windowWidth")]
    public double WindowWidth { get; set; }

    [JsonPropertyName("windowHeight")]
    public double WindowHeight { get; set; }

    [JsonPropertyName("windowMaximized")]
    public bool WindowMaximized { get; set; }
}

public sealed class SettingsService
{
    private static readonly Lazy<SettingsService> _instance = new(() => new SettingsService());
    public static SettingsService Instance => _instance.Value;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public string SettingsDir { get; }
    public string SettingsPath { get; }
    public AppSettings Current { get; private set; }

    public event Action? Changed;

    private readonly object _saveLock = new();

    private SettingsService()
    {
        SettingsDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ZapretGUI");
        SettingsPath = Path.Combine(SettingsDir, "settings.json");
        Current = Load();
    }

    private AppSettings Load()
    {
        try
        {
            if (!File.Exists(SettingsPath)) return new AppSettings();
            string json = File.ReadAllText(SettingsPath);
            return JsonSerializer.Deserialize<AppSettings>(json, JsonOpts) ?? new AppSettings();
        }
        catch { return new AppSettings(); }
    }

    public void Save()
    {
        lock (_saveLock)
        {
            try
            {
                Directory.CreateDirectory(SettingsDir);
                string json = JsonSerializer.Serialize(Current, JsonOpts);

                // Write to a sibling temp file then atomically replace, so a crash
                // mid-write cannot leave settings.json half-written / corrupt.
                string tmp = SettingsPath + ".tmp";
                File.WriteAllText(tmp, json);
                if (File.Exists(SettingsPath))
                    File.Replace(tmp, SettingsPath, destinationBackupFileName: null, ignoreMetadataErrors: true);
                else
                    File.Move(tmp, SettingsPath);
            }
            catch { return; }
        }
        Changed?.Invoke();
    }

    public void Update(Action<AppSettings> mutator)
    {
        lock (_saveLock)
        {
            mutator(Current);
        }
        Save();
    }
}
