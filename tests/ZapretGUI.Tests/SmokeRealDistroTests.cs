using System.IO;
using Xunit;
using Xunit.Abstractions;
using ZapretGUI.Models;
using ZapretGUI.Services;

namespace ZapretGUI.Tests;

/// <summary>
/// End-to-end smoke: instantiate ZapretController against a real on-disk ZDefree
/// distro and verify the provider switches, strategies load, and a sample args
/// extraction works. Path is read from the SMOKE_ZDEFREE_ROOT env var; the test
/// is skipped if it's not set or doesn't exist (so this won't run on CI by
/// default — it's a local smoke trigger).
///
/// Run from CLI:
///   $env:SMOKE_ZDEFREE_ROOT = "C:\path\to\zdefree-install"
///   dotnet test --filter "FullyQualifiedName~SmokeRealDistroTests" -v normal
/// </summary>
public class SmokeRealDistroTests
{
    private readonly ITestOutputHelper _output;

    public SmokeRealDistroTests(ITestOutputHelper output) { _output = output; }

    private static string? Root() => Environment.GetEnvironmentVariable("SMOKE_ZDEFREE_ROOT");

    [Fact]
    public void Real_distro_picks_ZDefree_provider_and_lists_strategies()
    {
        string? root = Root();
        if (string.IsNullOrEmpty(root) || !Directory.Exists(root))
        {
            _output.WriteLine($"SKIP: SMOKE_ZDEFREE_ROOT not set or path missing (got: {root ?? "<null>"})");
            return;
        }

        _output.WriteLine($"Smoke root: {root}");
        _output.WriteLine($"IsZapretRoot:  {ZapretController.IsZapretRoot(root)}");
        _output.WriteLine($"IsZDefreeRoot: {ZapretController.IsZDefreeRoot(root)}");

        var ctrl = new ZapretController(root);

        Assert.True(ctrl.IsZapretInstalled, "ZapretController didn't see a valid install");
        Assert.Equal("ZDefree", ctrl.Strategies.ProviderName);

        var list = ctrl.Strategies.Discover();
        _output.WriteLine($"Provider:   {ctrl.Strategies.ProviderName}");
        _output.WriteLine($"Strategies: {list.Count}");
        foreach (var s in list.Take(5))
        {
            _output.WriteLine($"  - {s.FileName,-40} cat={s.Category,-12} name=\"{s.DisplayName}\"");
        }

        Assert.NotEmpty(list);

        // Sample arg extraction — pick the first strategy, run a real winws compile.
        var sample = list[0];
        var filter = new GameFilterState(GameFilterMode.Tcp, "1024-65535", "27015", "27015");
        string args = ctrl.Strategies.ExtractWinwsArgs(sample, filter);
        _output.WriteLine($"\nSample compile ({sample.FileName}):");
        _output.WriteLine($"  args length: {args.Length} chars");
        _output.WriteLine($"  preview:     {args.Substring(0, Math.Min(140, args.Length))}...");

        Assert.NotEmpty(args);
        Assert.Contains("--dpi-desync", args);
    }
}
