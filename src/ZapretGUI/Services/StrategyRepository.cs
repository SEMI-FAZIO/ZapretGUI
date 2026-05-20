using System.IO;
using System.Text.RegularExpressions;
using ZapretGUI.Models;

namespace ZapretGUI.Services;

public sealed class StrategyRepository
{
    private readonly string _root;

    public StrategyRepository(string zapretRoot) => _root = zapretRoot;

    public IReadOnlyList<Strategy> Discover()
    {
        var files = Directory.EnumerateFiles(_root, "*.bat", SearchOption.TopDirectoryOnly)
            .Where(f =>
            {
                string name = Path.GetFileName(f);
                if (name.StartsWith("service", StringComparison.OrdinalIgnoreCase)) return false;
                if (name.Equals("ZapretGUI.bat", StringComparison.OrdinalIgnoreCase)) return false;
                try
                {
                    string text = File.ReadAllText(f);
                    return text.Contains("winws.exe", StringComparison.OrdinalIgnoreCase);
                }
                catch { return false; }
            })
            .OrderBy(NaturalOrderKey, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return files.Select(f => new Strategy
        {
            FileName = Path.GetFileName(f),
            FullPath = f,
            DisplayName = BatchParser.MakeDisplayName(f),
            Category = BatchParser.ExtractCategory(f) ?? "GENERAL",
            Description = BuildDescription(Path.GetFileName(f)),
        }).ToList();
    }

    private static string NaturalOrderKey(string s)
        => Regex.Replace(s, @"\d+", m => m.Value.PadLeft(8, '0'));

    private static string BuildDescription(string fileName)
    {
        string name = Path.GetFileNameWithoutExtension(fileName);
        if (name.Equals("general", StringComparison.OrdinalIgnoreCase))
            return "Стратегия по умолчанию — рекомендуется в первую очередь";
        if (name.Contains("FAKE TLS AUTO", StringComparison.OrdinalIgnoreCase))
            return "Авто-определение Fake TLS — для провайдеров с DPI на TLS";
        if (name.Contains("FAKE TLS", StringComparison.OrdinalIgnoreCase))
            return "Fake TLS ClientHello — обход TLS-фильтрации";
        if (name.Contains("SIMPLE FAKE", StringComparison.OrdinalIgnoreCase))
            return "Простой fake без overlap — для базовых случаев";
        if (name.Contains("ALT", StringComparison.OrdinalIgnoreCase))
            return "Альтернативные параметры desync — если общая стратегия не работает";
        return "Альтернативная стратегия обхода";
    }
}
