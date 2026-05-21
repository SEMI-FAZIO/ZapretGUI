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

        byte[] bytes = File.ReadAllBytes(batPath);
        string text = DecodeBytes(bytes);

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

    /// <summary>
    /// Decodes a .bat file. Tries, in order:
    ///   1. UTF-8 / UTF-16 BOM — trust the BOM.
    ///   2. Strict UTF-8 decode — accept if every byte is valid UTF-8.
    ///   3. Cp866 fallback — legacy Russian DOS encoding, the default for
    ///      .bat files on a Russian-locale Windows for ~25 years.
    /// </summary>
    internal static string DecodeBytes(byte[] bytes)
    {
        if (bytes.Length == 0) return string.Empty;

        // BOM checks
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            return new UTF8Encoding(false).GetString(bytes, 3, bytes.Length - 3);
        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
            return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
            return Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);

        // Strict UTF-8 — throws on invalid byte sequences.
        var strictUtf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
        try
        {
            return strictUtf8.GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            // Not valid UTF-8 — fall back to cp866. Requires
            // Encoding.RegisterProvider(CodePagesEncodingProvider.Instance)
            // which App.OnStartup does at process boot.
            return Encoding.GetEncoding(866).GetString(bytes);
        }
    }
}
