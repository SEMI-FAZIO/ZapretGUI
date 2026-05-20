namespace ZapretGUI.Models;

public sealed class Strategy
{
    public required string FileName { get; init; }
    public required string FullPath { get; init; }
    public required string DisplayName { get; init; }
    public string? Category { get; init; }
    public string? Description { get; init; }
}

public enum GameFilterMode
{
    Disabled,
    All,
    Tcp,
    Udp,
}

public enum IpsetMode
{
    Loaded,
    None,
    Any,
}

public enum BypassRunState
{
    Stopped,
    RunningStandalone,
    RunningAsService,
}

public sealed class ZapretStatus
{
    public BypassRunState RunState { get; init; }
    public string? CurrentStrategy { get; init; }
    public bool ServiceInstalled { get; init; }
    public bool WinDivertActive { get; init; }
    public bool WinwsRunning { get; init; }
    public GameFilterMode GameFilter { get; init; }
    public IpsetMode Ipset { get; init; }
    public bool AutoUpdateEnabled { get; init; }
}

public sealed class DiagnosticResult
{
    public required string Name { get; init; }
    public required DiagnosticLevel Level { get; init; }
    public required string Message { get; init; }
    public string? Hint { get; init; }
}

public enum DiagnosticLevel
{
    Ok,
    Warning,
    Error,
    Info,
}
