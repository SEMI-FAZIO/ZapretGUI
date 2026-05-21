using System.IO;
using System.IO.Compression;
using System.Text;
using Xunit;
using ZapretGUI.Services;

namespace ZapretGUI.Tests;

public class BackupImportZipSlipTests : IDisposable
{
    private readonly string _root;
    private readonly string _zapretRoot;

    public BackupImportZipSlipTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "zapretgui-backup-test-" + Guid.NewGuid().ToString("N"));
        _zapretRoot = Path.Combine(_root, "zapret");
        Directory.CreateDirectory(_zapretRoot);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    [Fact]
    public void Import_RejectsZipSlipInZapretPrefix_PositiveAndNegativeCoexist()
    {
        string zipPath = Path.Combine(_root, "mixed.zip");
        using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            // Legit entry — should be restored
            AddEntry(archive, "zapret/lists/list-general-user.txt", "discord.com\n");

            // Evil entry — resolves to _root/evil.txt, outside the zapret root
            AddEntry(archive, "zapret/../evil.txt", "pwned\n");

            // Deeper evil — resolves further outside
            AddEntry(archive, "zapret/lists/../../../evil2.txt", "pwned\n");
        }

        var ctrl = new ZapretController(_zapretRoot);
        var svc = new BackupService(ctrl);

        var result = svc.Import(zipPath);

        // Positive: legit entry restored
        Assert.True(File.Exists(Path.Combine(_zapretRoot, "lists", "list-general-user.txt")),
            "Legit entry should be extracted");
        Assert.Contains("lists/list-general-user.txt", result.RestoredFiles);

        // Negative: evil entries must NOT have been written anywhere outside _zapretRoot
        Assert.False(File.Exists(Path.Combine(_root, "evil.txt")),
            "Zip-Slip entry must not have escaped to " + Path.Combine(_root, "evil.txt"));

        string parentOfRoot = Path.GetDirectoryName(_root)!;
        Assert.False(File.Exists(Path.Combine(parentOfRoot, "evil2.txt")),
            "Deeper Zip-Slip entry must not have escaped");
    }

    [Fact]
    public void Import_LegitimateBackupRoundTripWorks()
    {
        // Positive control: a normal backup should restore cleanly. Proves the
        // Zip-Slip guard didn't accidentally reject safe entries.
        string zipPath = Path.Combine(_root, "good.zip");
        using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            AddEntry(archive, "zapret/lists/list-general-user.txt", "discord.com\nyoutube.com\n");
            AddEntry(archive, "zapret/lists/list-exclude-user.txt", "example.com\n");
            AddEntry(archive, "zapret/utils/game_filter.enabled", "");
        }

        var ctrl = new ZapretController(_zapretRoot);
        var svc = new BackupService(ctrl);

        var result = svc.Import(zipPath);

        Assert.Equal(3, result.RestoredFiles.Count);
        Assert.Equal("discord.com\nyoutube.com\n",
            File.ReadAllText(Path.Combine(_zapretRoot, "lists", "list-general-user.txt")));
        Assert.True(File.Exists(Path.Combine(_zapretRoot, "utils", "game_filter.enabled")));
    }

    private static void AddEntry(ZipArchive archive, string entryName, string content)
    {
        var entry = archive.CreateEntry(entryName);
        using var stream = entry.Open();
        byte[] bytes = Encoding.UTF8.GetBytes(content);
        stream.Write(bytes, 0, bytes.Length);
    }
}
