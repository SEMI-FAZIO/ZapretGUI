using System.IO;
using ZapretGUI.Models;
using ZDefreeCompilation = ZDefree.Core.Compilation;
using ZDefreeSerialization = ZDefree.Core.Serialization;

namespace ZapretGUI.Services;

/// <summary>
/// Native ZDefree strategy source: reads <c>strategies/INDEX.json</c> + the
/// individual <c>strategies/&lt;file&gt;.json</c> via <c>ZDefree.Core</c>, and
/// compiles each strategy into a winws.exe command line using
/// <c>WinwsCompiler</c>. Active when <c>manifest.json</c> is present in the
/// install root.
/// </summary>
public sealed class ZDefreeStrategyProvider : IStrategyProvider
{
    private readonly string _root;
    private readonly string _lang;

    /// <param name="root">ZDefree install root (contains manifest.json, strategies/, lists/, bin/).</param>
    /// <param name="lang">UI language for resolving i18n descriptions ("en" / "ru" / …). Defaults to "en".</param>
    public ZDefreeStrategyProvider(string root, string lang = "en")
    {
        _root = root;
        _lang = lang;
    }

    public string ProviderName => "ZDefree";

    public IReadOnlyList<Strategy> Discover()
    {
        string strategiesDir = Path.Combine(_root, "strategies");
        string indexPath     = Path.Combine(strategiesDir, "INDEX.json");

        if (!File.Exists(indexPath))
        {
            // No INDEX.json yet — fall back to scanning common/ + advanced/ ourselves
            // (frontends should never depend on INDEX being present, but it is the
            // contract; absence means the bootstrap hasn't been run yet).
            return DiscoverByScan(strategiesDir);
        }

        var index = ZDefreeSerialization.StrategyIndexBuilder.Deserialize(File.ReadAllText(indexPath));
        var result = new List<Strategy>(index.Strategies.Count);
        foreach (var entry in index.Strategies)
        {
            string full = Path.Combine(strategiesDir, entry.File.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(full)) continue;

            result.Add(new Strategy
            {
                FileName    = Path.GetFileName(full),
                FullPath    = full,
                DisplayName = entry.Name,
                Category    = NormalizeCategory(entry.Category),
                Description = entry.Description?.Resolve(_lang),
            });
        }
        return result;
    }

    public string ExtractWinwsArgs(Strategy strategy, GameFilterState filter)
    {
        var loaded = ZDefreeSerialization.StrategyLoader.LoadFromFile(strategy.FullPath);
        var opts = new ZDefreeCompilation.CompileOptions
        {
            BinDir       = Path.Combine(_root, "bin") + Path.DirectorySeparatorChar,
            ListsDir     = Path.Combine(_root, "lists", "bundled") + Path.DirectorySeparatorChar,
            GameTcpPorts = filter.Tcp,
            GameUdpPorts = filter.Udp,
        };
        return new ZDefreeCompilation.WinwsCompiler(opts).Compile(loaded);
    }

    private IReadOnlyList<Strategy> DiscoverByScan(string strategiesDir)
    {
        var result = new List<Strategy>();
        foreach (var subdir in new[] { "common", "advanced" })
        {
            string dir = Path.Combine(strategiesDir, subdir);
            if (!Directory.Exists(dir)) continue;
            foreach (var f in Directory.EnumerateFiles(dir, "*.json", SearchOption.TopDirectoryOnly))
            {
                string name = Path.GetFileName(f);
                if (name.Equals("INDEX.json", StringComparison.OrdinalIgnoreCase)) continue;
                if (name.EndsWith(".schema.json", StringComparison.OrdinalIgnoreCase)) continue;
                try
                {
                    var loaded = ZDefreeSerialization.StrategyLoader.LoadFromFile(f);
                    result.Add(new Strategy
                    {
                        FileName    = name,
                        FullPath    = f,
                        DisplayName = loaded.Name,
                        Category    = NormalizeCategory(loaded.Category),
                        Description = loaded.Description?.Resolve(_lang),
                    });
                }
                catch { /* skip broken files; don't poison the whole catalog */ }
            }
        }
        return result;
    }

    private static string NormalizeCategory(string? category)
    {
        // ZapretGUI's categories are uppercase ("GENERAL", "ALT", "FAKE TLS") to
        // match Flowseal naming. ZDefree uses lowercase kebab. Bridge here.
        if (string.IsNullOrEmpty(category)) return "GENERAL";
        return category.ToUpperInvariant().Replace('-', ' ');
    }
}
