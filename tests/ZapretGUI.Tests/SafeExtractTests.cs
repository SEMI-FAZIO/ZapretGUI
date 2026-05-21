using System.IO;
using System.IO.Compression;
using System.Text;
using Xunit;
using ZapretGUI.Services;

namespace ZapretGUI.Tests;

public class SafeExtractTests : IDisposable
{
    private readonly string _root;

    public SafeExtractTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "zapretgui-extract-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    [Fact]
    public void SafeExtract_RejectsForwardSlashTraversal()
    {
        string zipPath = Path.Combine(_root, "evil.zip");
        string extractTo = Path.Combine(_root, "extract");
        Directory.CreateDirectory(extractTo);

        using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            AddEntry(archive, "../escaped.txt", "pwned");
        }

        var ex = Assert.Throws<InvalidOperationException>(
            () => ZapretInstaller.SafeExtractToDirectory(zipPath, extractTo));
        Assert.Contains("Небезопасный", ex.Message);

        Assert.False(File.Exists(Path.Combine(_root, "escaped.txt")),
            "Zip-Slip entry must not have been written outside extract root");
    }

    [Fact]
    public void SafeExtract_RejectsBackslashTraversal()
    {
        string zipPath = Path.Combine(_root, "evil.zip");
        string extractTo = Path.Combine(_root, "extract");
        Directory.CreateDirectory(extractTo);

        using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            AddEntry(archive, @"..\escaped.txt", "pwned");
        }

        Assert.Throws<InvalidOperationException>(
            () => ZapretInstaller.SafeExtractToDirectory(zipPath, extractTo));
        Assert.False(File.Exists(Path.Combine(_root, "escaped.txt")));
    }

    [Fact]
    public void SafeExtract_RejectsWindowsRootedPath()
    {
        string zipPath = Path.Combine(_root, "evil.zip");
        string extractTo = Path.Combine(_root, "extract");
        Directory.CreateDirectory(extractTo);

        using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            // After Replace('\\','/') and TrimStart('/'), this becomes "C:/Windows/evil.txt",
            // which Path.Combine treats as rooted and resolves outside extractTo.
            AddEntry(archive, @"C:\Windows\evil.txt", "pwned");
        }

        Assert.Throws<InvalidOperationException>(
            () => ZapretInstaller.SafeExtractToDirectory(zipPath, extractTo));
    }

    [Fact]
    public void SafeExtract_RejectsNestedTraversal()
    {
        string zipPath = Path.Combine(_root, "evil.zip");
        string extractTo = Path.Combine(_root, "extract");
        Directory.CreateDirectory(extractTo);

        using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            // Looks deep but resolves to extract/../escaped.txt = _root/escaped.txt
            AddEntry(archive, "lists/../../escaped.txt", "pwned");
        }

        Assert.Throws<InvalidOperationException>(
            () => ZapretInstaller.SafeExtractToDirectory(zipPath, extractTo));
        Assert.False(File.Exists(Path.Combine(_root, "escaped.txt")));
    }

    [Fact]
    public void SafeExtract_AllowsLegitimateNestedEntries()
    {
        string zipPath = Path.Combine(_root, "good.zip");
        string extractTo = Path.Combine(_root, "extract");
        Directory.CreateDirectory(extractTo);

        using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            AddEntry(archive, "bin/winws.exe", "MZ-fake");
            AddEntry(archive, "lists/list-general.txt", "discord.com\n");
            AddEntry(archive, "deep/nested/path/file.txt", "ok");
        }

        ZapretInstaller.SafeExtractToDirectory(zipPath, extractTo);

        Assert.True(File.Exists(Path.Combine(extractTo, "bin", "winws.exe")));
        Assert.True(File.Exists(Path.Combine(extractTo, "lists", "list-general.txt")));
        Assert.True(File.Exists(Path.Combine(extractTo, "deep", "nested", "path", "file.txt")));
    }

    private static void AddEntry(ZipArchive archive, string entryName, string content)
    {
        // Open file then write raw — ZipArchive.CreateEntry preserves the entry name as-is,
        // including unusual characters needed for Zip-Slip test inputs.
        var entry = archive.CreateEntry(entryName);
        using var stream = entry.Open();
        byte[] bytes = Encoding.UTF8.GetBytes(content);
        stream.Write(bytes, 0, bytes.Length);
    }
}
