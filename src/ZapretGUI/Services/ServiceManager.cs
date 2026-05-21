using System.Diagnostics;
using System.IO;
using System.ServiceProcess;
using Microsoft.Win32;
using ZapretGUI.Models;

namespace ZapretGUI.Services;

public sealed class ServiceManager
{
    public const string ZapretServiceName = "zapret";
    public const string WinDivertServiceName = "WinDivert";

    private readonly string _root;
    private readonly FilterManager _filters;
    private readonly IStrategyProvider _provider;

    public ServiceManager(string zapretRoot, FilterManager filters, IStrategyProvider provider)
    {
        _root = zapretRoot;
        _filters = filters;
        _provider = provider;
    }

    public bool IsServiceInstalled() => QueryService(ZapretServiceName) is not null;

    public bool IsServiceRunning() =>
        QueryService(ZapretServiceName) is ServiceControllerStatus s
        && s == ServiceControllerStatus.Running;

    public bool IsWinDivertActive()
    {
        var s = QueryService(WinDivertServiceName);
        return s == ServiceControllerStatus.Running || s == ServiceControllerStatus.StopPending;
    }

    public bool IsWinwsProcessRunning() => Process.GetProcessesByName("winws").Length > 0;

    public string? GetInstalledStrategyName()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"System\CurrentControlSet\Services\zapret");
            return key?.GetValue("zapret-discord-youtube") as string;
        }
        catch { return null; }
    }

    public static ServiceControllerStatus? QueryService(string name)
    {
        try
        {
            using var sc = new ServiceController(name);
            return sc.Status;
        }
        catch { return null; }
    }

    public async Task<string> InstallAsServiceAsync(Strategy strategy, CancellationToken ct = default)
    {
        string args = _provider.ExtractWinwsArgs(strategy, _filters.GetGameFilterState());

        await StopAndDeleteAsync(ZapretServiceName, ct);
        KillWinws();
        EnableTcpTimestamps();

        string exe = Path.Combine(_root, "bin", "winws.exe");
        string binPath = $"\"{exe}\" {args}";

        var result = await Task.Run(() => Win32ServiceInstaller.Install(
            ZapretServiceName,
            displayName: "zapret",
            description: "Zapret DPI bypass — installed by ZapretGUI",
            binPathWithArgs: binPath,
            autoStart: true,
            startNow: true), ct);

        using var key = Registry.LocalMachine.CreateSubKey(@"System\CurrentControlSet\Services\zapret");
        key?.SetValue("zapret-discord-youtube", Path.GetFileNameWithoutExtension(strategy.FileName), RegistryValueKind.String);

        if (!result.Started)
            return $"Сервис создан со стратегией «{strategy.DisplayName}», но не стартовал (Win32 err {result.StartErrorCode}). Перезагрузка решит — режим auto-start уже выставлен.";

        return $"Сервис «{ZapretServiceName}» создан, запущен и помечен auto-start (с авто-восстановлением). Стратегия: «{strategy.DisplayName}».";
    }

    public async Task<string> RemoveServiceAsync(CancellationToken ct = default)
    {
        await StopAndDeleteAsync(ZapretServiceName, ct);
        KillWinws();

        if (QueryService(WinDivertServiceName) is not null)
            await StopAndDeleteAsync(WinDivertServiceName, ct);

        if (QueryService("WinDivert14") is not null)
            await StopAndDeleteAsync("WinDivert14", ct);

        return "Сервисы zapret и WinDivert удалены, winws.exe остановлен.";
    }

    public async Task<string> RunStandaloneAsync(Strategy strategy, LogStreamService? logs = null, bool captureLogs = false, CancellationToken ct = default)
    {
        if (IsServiceRunning())
            throw new InvalidOperationException("Сервис zapret уже запущен. Удалите его перед запуском в режиме standalone.");

        KillWinws();
        _filters.EnsureUserLists();
        EnableTcpTimestamps();

        string args = _provider.ExtractWinwsArgs(strategy, _filters.GetGameFilterState());
        string exe = Path.Combine(_root, "bin", "winws.exe");

        if (captureLogs && logs is not null)
        {
            var psi = new ProcessStartInfo
            {
                FileName = exe,
                Arguments = args,
                WorkingDirectory = Path.Combine(_root, "bin"),
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = System.Text.Encoding.UTF8,
                StandardErrorEncoding = System.Text.Encoding.UTF8,
            };
            var p = new Process { StartInfo = psi, EnableRaisingEvents = true };
            p.Start();
            logs.Attach(p);
        }
        else
        {
            var psi = new ProcessStartInfo
            {
                FileName = exe,
                Arguments = args,
                WorkingDirectory = Path.Combine(_root, "bin"),
                UseShellExecute = true,
                WindowStyle = ProcessWindowStyle.Minimized,
                CreateNoWindow = false,
            };
            Process.Start(psi);
        }

        await Task.Delay(400, ct);
        return $"winws.exe запущен со стратегией «{strategy.DisplayName}».";
    }

    public string StopStandalone()
    {
        if (!IsWinwsProcessRunning()) return "winws.exe не запущен.";
        KillWinws();
        return "winws.exe остановлен.";
    }

    public static void KillWinws()
    {
        foreach (var p in Process.GetProcessesByName("winws"))
        {
            try { p.Kill(true); p.WaitForExit(2000); } catch { }
        }
    }

    public static void EnableTcpTimestamps()
    {
        try
        {
            var psi = new ProcessStartInfo("netsh", "interface tcp set global timestamps=enabled")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            using var p = Process.Start(psi);
            p?.WaitForExit(3000);
        }
        catch { }
    }

    private static async Task StopAndDeleteAsync(string name, CancellationToken ct)
    {
        try
        {
            using var sc = new ServiceController(name);
            if (sc.Status != ServiceControllerStatus.Stopped)
            {
                sc.Stop();
                sc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(8));
            }
        }
        catch { }
        await RunScAsync($"delete {name}", ct);
    }

    private static async Task<(int code, string stdout, string stderr)> RunScAsync(string args, CancellationToken ct)
    {
        var psi = new ProcessStartInfo("sc.exe")
        {
            Arguments = args,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = System.Text.Encoding.UTF8,
            StandardErrorEncoding = System.Text.Encoding.UTF8,
        };

        try
        {
            using var p = Process.Start(psi);
            if (p is null) return (-1, "", "Process.Start returned null");
            string stdout = await p.StandardOutput.ReadToEndAsync(ct);
            string stderr = await p.StandardError.ReadToEndAsync(ct);
            await p.WaitForExitAsync(ct);
            return (p.ExitCode, stdout, stderr);
        }
        catch (Exception ex)
        {
            return (-1, "", ex.Message);
        }
    }

}
