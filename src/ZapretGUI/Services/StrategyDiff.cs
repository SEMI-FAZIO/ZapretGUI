using System.IO;
using System.Text.RegularExpressions;
using ZapretGUI.Models;

namespace ZapretGUI.Services;

public enum DiffKind { Added, Removed, Changed, Same }

public sealed record StrategyDiffLine(string Key, string? Left, string? Right, DiffKind Kind);

/// <summary>
/// Computes a key-by-key diff between the winws arguments of two strategy .bat files.
/// Used to highlight what makes an ALT variant different from "general".
/// </summary>
public static class StrategyDiffEngine
{
    public static IReadOnlyList<StrategyDiffLine> Diff(Strategy left, Strategy right, FilterManager filters, string root)
    {
        var (_, all, tcp, udp) = filters.GetGameFilter();
        string leftArgs  = SafeParse(() => BatchParser.ExtractWinwsArgs(left.FullPath,  root, all, tcp, udp));
        string rightArgs = SafeParse(() => BatchParser.ExtractWinwsArgs(right.FullPath, root, all, tcp, udp));

        var leftMap  = ParseArgs(leftArgs);
        var rightMap = ParseArgs(rightArgs);

        var keys = new SortedSet<string>(leftMap.Keys.Union(rightMap.Keys), StringComparer.Ordinal);
        var lines = new List<StrategyDiffLine>(keys.Count);

        foreach (var key in keys)
        {
            leftMap.TryGetValue(key, out var l);
            rightMap.TryGetValue(key, out var r);

            DiffKind kind;
            if (l is null && r is not null) kind = DiffKind.Added;
            else if (l is not null && r is null) kind = DiffKind.Removed;
            else if (!string.Equals(l, r, StringComparison.Ordinal)) kind = DiffKind.Changed;
            else kind = DiffKind.Same;

            lines.Add(new StrategyDiffLine(key, l, r, kind));
        }

        return lines;
    }

    public static int CountDifferences(IReadOnlyList<StrategyDiffLine> diff)
        => diff.Count(l => l.Kind != DiffKind.Same);

    public static Strategy? FindBaseStrategy(IEnumerable<Strategy> all)
    {
        return all.FirstOrDefault(s =>
            Path.GetFileNameWithoutExtension(s.FileName).Equals("general", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Tokenizes a winws.exe command line into (key, full-text) pairs. winws uses repeated `--filter-tcp=…`
    /// blocks separated by `--new`; we treat each block as numbered (filter#0, filter#1, …) so positionally
    /// similar blocks line up across strategies.
    /// </summary>
    private static Dictionary<string, string> ParseArgs(string args)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(args)) return result;

        // Split into blocks separated by `--new`
        string[] blocks = Regex.Split(args, @"\s+--new\b");
        for (int b = 0; b < blocks.Length; b++)
        {
            string block = blocks[b].Trim();
            string blockPrefix = $"block#{b:00}";
            var tokens = TokenizeArgs(block);
            foreach (var (key, value) in tokens)
            {
                string fullKey = $"{blockPrefix}.{key}";
                if (result.ContainsKey(fullKey))
                    result[fullKey] += " | " + value;
                else
                    result[fullKey] = value;
            }
        }
        return result;
    }

    private static IEnumerable<(string key, string value)> TokenizeArgs(string args)
    {
        // Match --key=value or --key value, with value optionally quoted.
        var rx = new Regex(@"--([a-zA-Z0-9-]+)(?:[=\s]+((?:""[^""]*""|\S+)))?", RegexOptions.Compiled);
        foreach (Match m in rx.Matches(args))
        {
            string k = m.Groups[1].Value;
            string v = m.Groups[2].Success ? m.Groups[2].Value.Trim('"') : "";
            yield return (k, v);
        }
    }

    private static string SafeParse(Func<string> f)
    {
        try { return f(); } catch { return string.Empty; }
    }
}
