using System.IO;
using System.IO.Compression;
using System.Text.Json;

namespace ZapretGUI.Services;

/// <summary>
/// Backs up and restores user-editable bits: settings.json, user lists,
/// game_filter / check_updates flags. Excludes the bypass binaries.
/// </summary>
public sealed class BackupService
{
    private readonly ZapretController _ctrl;
    public BackupService(ZapretController ctrl) => _ctrl = ctrl;

    private static readonly string[] BackedUpRelativePaths =
    [
        "lists/list-general-user.txt",
        "lists/list-exclude-user.txt",
        "lists/ipset-exclude-user.txt",
        "utils/game_filter.enabled",
        "utils/check_updates.enabled",
    ];

    public void Export(string destZip)
    {
        if (File.Exists(destZip)) File.Delete(destZip);
        using var archive = ZipFile.Open(destZip, ZipArchiveMode.Create);

        // Settings
        var settingsBytes = File.Exists(SettingsService.Instance.SettingsPath)
            ? File.ReadAllBytes(SettingsService.Instance.SettingsPath)
            : System.Text.Encoding.UTF8.GetBytes("{}");
        WriteEntry(archive, "settings.json", settingsBytes);

        // Manifest with version + zapret root for context (not auto-applied on import).
        var manifest = JsonSerializer.SerializeToUtf8Bytes(new
        {
            createdAt = DateTime.UtcNow.ToString("o"),
            guiVersion = "1.0.0",
            zapretRoot = _ctrl.ZapretRoot,
            entries = BackedUpRelativePaths,
        }, new JsonSerializerOptions { WriteIndented = true });
        WriteEntry(archive, "manifest.json", manifest);

        // User files
        foreach (var rel in BackedUpRelativePaths)
        {
            string src = Path.Combine(_ctrl.ZapretRoot, rel.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(src)) continue;
            byte[] bytes = File.ReadAllBytes(src);
            WriteEntry(archive, "zapret/" + rel, bytes);
        }
    }

    public BackupRestoreResult Import(string sourceZip)
    {
        var result = new BackupRestoreResult();
        using var archive = ZipFile.OpenRead(sourceZip);

        foreach (var entry in archive.Entries)
        {
            if (entry.FullName.Equals("settings.json", StringComparison.OrdinalIgnoreCase))
            {
                string target = SettingsService.Instance.SettingsPath;
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                entry.ExtractToFile(target, overwrite: true);
                result.RestoredSettings = true;
                continue;
            }

            if (entry.FullName.StartsWith("zapret/", StringComparison.OrdinalIgnoreCase))
            {
                string rel = entry.FullName.Substring("zapret/".Length);
                string target = Path.Combine(_ctrl.ZapretRoot, rel.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                entry.ExtractToFile(target, overwrite: true);
                result.RestoredFiles.Add(rel);
            }
        }
        return result;
    }

    private static void WriteEntry(ZipArchive archive, string name, byte[] bytes)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.Optimal);
        using var s = entry.Open();
        s.Write(bytes, 0, bytes.Length);
    }
}

public sealed class BackupRestoreResult
{
    public bool RestoredSettings { get; set; }
    public List<string> RestoredFiles { get; } = new();
}
