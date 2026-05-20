using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace ZapretGUI.Services;

/// <summary>
/// Parses zapret .bat files to extract the winws.exe invocation.
/// Mirrors the logic from service.bat :service_install — joins line-continued args,
/// substitutes %BIN%, %LISTS%, %GameFilter*% and quoting.
/// </summary>
public static class BatchParser
{
    public static string ExtractWinwsArgs(string batPath, string zapretRoot, string gameFilterAll, string gameFilterTcp, string gameFilterUdp)
    {
        string binDir = Path.Combine(zapretRoot, "bin") + "\\";
        string listsDir = Path.Combine(zapretRoot, "lists") + "\\";

        string text = File.ReadAllText(batPath, DetectEncoding(batPath));

        var sb = new StringBuilder();
        bool capturing = false;
        bool foundExe = false;

        foreach (var rawLine in text.Split('\n'))
        {
            string line = rawLine.TrimEnd('\r').TrimEnd();
            if (!capturing)
            {
                int idx = line.IndexOf("winws.exe\"", StringComparison.OrdinalIgnoreCase);
                if (idx < 0) idx = line.IndexOf("winws.exe", StringComparison.OrdinalIgnoreCase);
                if (idx < 0) continue;

                int after = line.IndexOf("winws.exe", idx, StringComparison.OrdinalIgnoreCase) + "winws.exe".Length;
                if (after < line.Length && line[after] == '"') after++;
                string tail = line.Substring(after).TrimEnd('^').Trim();
                sb.Append(tail).Append(' ');
                capturing = true;
                foundExe = true;
                continue;
            }

            bool continued = line.EndsWith("^");
            string content = continued ? line.Substring(0, line.Length - 1) : line;
            sb.Append(content.Trim()).Append(' ');
            if (!continued) break;
        }

        if (!foundExe)
            throw new InvalidOperationException($"winws.exe invocation not found in {Path.GetFileName(batPath)}");

        string args = sb.ToString().Trim();

        args = args.Replace("%BIN%", binDir, StringComparison.OrdinalIgnoreCase);
        args = args.Replace("%LISTS%", listsDir, StringComparison.OrdinalIgnoreCase);
        args = args.Replace("%GameFilterTCP%", gameFilterTcp, StringComparison.OrdinalIgnoreCase);
        args = args.Replace("%GameFilterUDP%", gameFilterUdp, StringComparison.OrdinalIgnoreCase);
        args = args.Replace("%GameFilter%", gameFilterAll, StringComparison.OrdinalIgnoreCase);

        return Regex.Replace(args, @"\s+", " ").Trim();
    }

    public static string MakeDisplayName(string fileName)
    {
        string name = Path.GetFileNameWithoutExtension(fileName);
        return name;
    }

    public static string? ExtractCategory(string fileName)
    {
        string name = Path.GetFileNameWithoutExtension(fileName);
        var match = Regex.Match(name, @"\(([^)]+)\)");
        if (!match.Success) return null;
        string inner = match.Groups[1].Value.Trim();
        if (inner.StartsWith("ALT", StringComparison.OrdinalIgnoreCase)) return "ALT";
        if (inner.Contains("FAKE TLS", StringComparison.OrdinalIgnoreCase)) return "FAKE TLS";
        if (inner.Contains("SIMPLE FAKE", StringComparison.OrdinalIgnoreCase)) return "SIMPLE FAKE";
        return inner;
    }

    private static Encoding DetectEncoding(string path)
    {
        using var fs = File.OpenRead(path);
        Span<byte> bom = stackalloc byte[3];
        int read = fs.Read(bom);
        if (read >= 3 && bom[0] == 0xEF && bom[1] == 0xBB && bom[2] == 0xBF)
            return Encoding.UTF8;
        return Encoding.UTF8;
    }
}
