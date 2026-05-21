using System.IO;
using Xunit;
using ZapretGUI.Models;
using ZapretGUI.Services;

namespace ZapretGUI.Tests;

public class ZDefreeStrategyProviderTests
{
    private static (string root, IDisposable cleanup) MakeFixture()
    {
        string root = Path.Combine(Path.GetTempPath(), $"zdefree-prov-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(root, "strategies", "common"));
        Directory.CreateDirectory(Path.Combine(root, "lists", "bundled"));
        Directory.CreateDirectory(Path.Combine(root, "bin"));
        return (root, new DeleteOnDispose(root));
    }

    [Fact]
    public void Discover_returns_strategies_from_INDEX_json()
    {
        var (root, cleanup) = MakeFixture();
        using (cleanup)
        {
            string common = Path.Combine(root, "strategies", "common");
            File.WriteAllText(Path.Combine(common, "general.json"), SampleStrategyJson("general", "General", "general"));
            File.WriteAllText(Path.Combine(common, "alt-one.json"), SampleStrategyJson("alt-one", "Alt One", "alt"));
            File.WriteAllText(Path.Combine(root, "strategies", "INDEX.json"), SampleIndex());

            var provider = new ZDefreeStrategyProvider(root);
            var strategies = provider.Discover();

            Assert.Equal(2, strategies.Count);
            Assert.Contains(strategies, s => s.FileName == "general.json" && s.DisplayName == "General");
            Assert.Contains(strategies, s => s.FileName == "alt-one.json" && s.DisplayName == "Alt One");
        }
    }

    [Fact]
    public void Discover_normalizes_kebab_category_to_uppercase_words()
    {
        var (root, cleanup) = MakeFixture();
        using (cleanup)
        {
            string common = Path.Combine(root, "strategies", "common");
            File.WriteAllText(Path.Combine(common, "ft.json"), SampleStrategyJson("ft", "FT", "fake-tls"));
            File.WriteAllText(Path.Combine(root, "strategies", "INDEX.json"),
                IndexFor(("ft", "FT", "fake-tls", "common/ft.json")));

            var s = new ZDefreeStrategyProvider(root).Discover().Single();
            Assert.Equal("FAKE TLS", s.Category);
        }
    }

    [Fact]
    public void Discover_resolves_i18n_description_per_lang()
    {
        var (root, cleanup) = MakeFixture();
        using (cleanup)
        {
            string common = Path.Combine(root, "strategies", "common");
            string strat = """
                {
                  "id": "g", "name": "G", "version": 1,
                  "description": { "en": "English text", "ru": "Russian text" },
                  "filters": [{ "name":"m", "wf":{"tcp":"443"},
                    "rules":[{"match":{"tcp":"443"},"desync":{"mode":"fake"}}]}]
                }
                """;
            File.WriteAllText(Path.Combine(common, "g.json"), strat);
            // Real `zdefree index` copies description into INDEX.json — mirror that here.
            string index = """
                {
                  "schema_version": 1,
                  "generated_at": "2026-05-21T00:00:00Z",
                  "generator": "test",
                  "strategies": [{
                    "id": "g", "name": "G",
                    "description": { "en": "English text", "ru": "Russian text" },
                    "file": "common/g.json"
                  }]
                }
                """;
            File.WriteAllText(Path.Combine(root, "strategies", "INDEX.json"), index);

            Assert.Equal("English text", new ZDefreeStrategyProvider(root, "en").Discover().Single().Description);
            Assert.Equal("Russian text", new ZDefreeStrategyProvider(root, "ru").Discover().Single().Description);
        }
    }

    [Fact]
    public void Discover_falls_back_to_directory_scan_when_INDEX_missing()
    {
        var (root, cleanup) = MakeFixture();
        using (cleanup)
        {
            string common = Path.Combine(root, "strategies", "common");
            File.WriteAllText(Path.Combine(common, "a.json"), SampleStrategyJson("a", "A", "general"));
            File.WriteAllText(Path.Combine(common, "INDEX.json"), "{}");          // junk, must be skipped
            File.WriteAllText(Path.Combine(common, "x.schema.json"), "{}");        // junk, must be skipped

            // No INDEX.json at strategies/ root — fall back to scan.
            var s = new ZDefreeStrategyProvider(root).Discover();
            Assert.Single(s);
            Assert.Equal("a.json", s[0].FileName);
        }
    }

    [Fact]
    public void ExtractWinwsArgs_compiles_strategy_with_filter_substitution()
    {
        var (root, cleanup) = MakeFixture();
        using (cleanup)
        {
            string common = Path.Combine(root, "strategies", "common");
            string strat = """
                {
                  "id": "g", "name": "G", "version": 1,
                  "filters": [{ "name":"m", "wf":{"tcp":"{game_tcp}"},
                    "rules":[{"match":{"tcp":"{game_tcp}"},"desync":{"mode":"fake"}}]}]
                }
                """;
            string path = Path.Combine(common, "g.json");
            File.WriteAllText(path, strat);

            var provider = new ZDefreeStrategyProvider(root);
            string args = provider.ExtractWinwsArgs(
                new Strategy { FileName = "g.json", FullPath = path, DisplayName = "G" },
                new GameFilterState(GameFilterMode.Tcp, "1024-65535", "27015,27036", "12"));

            Assert.Contains("--wf-tcp=27015,27036",      args);
            Assert.Contains("--filter-tcp=27015,27036",  args);
            Assert.Contains("--dpi-desync=fake",         args);
        }
    }

    [Fact]
    public void ProviderName_is_ZDefree()
    {
        var (root, cleanup) = MakeFixture();
        using (cleanup)
        {
            Assert.Equal("ZDefree", new ZDefreeStrategyProvider(root).ProviderName);
        }
    }

    [Fact]
    public void IsZDefreeRoot_returns_true_when_manifest_present()
    {
        var (root, cleanup) = MakeFixture();
        using (cleanup)
        {
            File.WriteAllText(Path.Combine(root, "manifest.json"), "{\"distribution\":\"ZDefree\"}");
            Assert.True(ZapretController.IsZDefreeRoot(root));
        }
    }

    [Fact]
    public void IsZDefreeRoot_returns_false_when_manifest_absent_or_root_invalid()
    {
        var (root, cleanup) = MakeFixture();
        using (cleanup)
        {
            Assert.False(ZapretController.IsZDefreeRoot(root));   // no manifest
            Assert.False(ZapretController.IsZDefreeRoot(null));   // null
            Assert.False(ZapretController.IsZDefreeRoot(""));     // empty
        }
    }

    // ---- Fixtures ----

    private static string SampleStrategyJson(string id, string name, string? category) =>
        $$"""
        {
          "id": "{{id}}",
          "name": "{{name}}",
          {{(category is null ? "" : $"\"category\": \"{category}\",")}}
          "version": 1,
          "filters": [
            { "name": "main", "wf": { "tcp": "443" },
              "rules": [{ "match": { "tcp": "443" }, "desync": { "mode": "fake" } }]
            }
          ]
        }
        """;

    private static string SampleIndex() => IndexFor(
        ("general", "General", "general", "common/general.json"),
        ("alt-one", "Alt One", "alt",     "common/alt-one.json"));

    private static string IndexFor(params (string id, string name, string? cat, string file)[] entries)
    {
        var rows = entries.Select(e =>
            $$"""
            {
              "id": "{{e.id}}",
              "name": "{{e.name}}",
              {{(e.cat is null ? "" : $"\"category\": \"{e.cat}\",")}}
              "file": "{{e.file}}"
            }
            """);
        return $$"""
        {
          "schema_version": 1,
          "generated_at": "2026-05-21T00:00:00Z",
          "generator": "test",
          "strategies": [{{string.Join(",", rows)}}]
        }
        """;
    }

    private sealed class DeleteOnDispose : IDisposable
    {
        private readonly string _path;
        public DeleteOnDispose(string p) { _path = p; }
        public void Dispose() { try { Directory.Delete(_path, recursive: true); } catch { } }
    }
}
