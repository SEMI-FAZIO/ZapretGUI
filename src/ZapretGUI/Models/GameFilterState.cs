namespace ZapretGUI.Models;

/// <summary>
/// Resolved game-filter port ranges for a given <see cref="GameFilterMode"/>.
/// Used by <see cref="ZapretGUI.Services.IStrategyProvider.ExtractWinwsArgs"/>
/// so each provider gets one shaped value instead of a tuple.
/// </summary>
public sealed record GameFilterState(GameFilterMode Mode, string All, string Tcp, string Udp);
