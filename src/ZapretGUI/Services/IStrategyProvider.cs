using ZapretGUI.Models;

namespace ZapretGUI.Services;

/// <summary>
/// Abstraction over a strategy source. Two implementations:
///   * <see cref="StrategyRepository"/> — legacy Flowseal mode, parses <c>.bat</c> files.
///   * <c>ZDefreeStrategyProvider</c> (added in P2) — native ZDefree mode, reads
///     <c>strategies/INDEX.json</c> and <c>strategies/&lt;file&gt;.json</c> via
///     <c>ZDefree.Core</c>.
///
/// The active provider is picked by <see cref="ZapretController"/> based on
/// whether <c>manifest.json</c> is present in the install root.
/// </summary>
public interface IStrategyProvider
{
    /// <summary>Short identifier shown in the UI as a "mode" badge.</summary>
    string ProviderName { get; }

    /// <summary>Lists all available strategies for this install.</summary>
    IReadOnlyList<Strategy> Discover();

    /// <summary>
    /// Produces the winws.exe command-line arguments for the selected strategy,
    /// substituting the user's current game-filter port ranges.
    /// </summary>
    string ExtractWinwsArgs(Strategy strategy, GameFilterState filter);
}
