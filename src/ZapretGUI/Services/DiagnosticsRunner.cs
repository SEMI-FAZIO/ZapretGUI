using System.Diagnostics;
using System.IO;
using System.ServiceProcess;
using Microsoft.Win32;
using ZapretGUI.Models;

namespace ZapretGUI.Services;

public sealed class DiagnosticsRunner
{
    private readonly string _root;

    public DiagnosticsRunner(string zapretRoot) => _root = zapretRoot;

    public async Task<List<DiagnosticResult>> RunAllAsync(CancellationToken ct = default)
    {
        var results = new List<DiagnosticResult>();
        results.Add(CheckBfe());
        results.Add(CheckProxy());
        results.Add(CheckTcpTimestamps());
        results.Add(CheckProcess("AdguardSvc", "Adguard может мешать Discord", "https://github.com/Flowseal/zapret-discord-youtube/issues/417"));
        results.Add(CheckServiceContains("Killer", "Killer-сервисы конфликтуют с zapret"));
        results.Add(CheckIntelConnectivity());
        results.Add(CheckCheckPoint());
        results.Add(CheckServiceContains("SmartByte", "SmartByte конфликтует с zapret"));
        results.Add(CheckWinDivertFile());
        results.Add(CheckVpn());
        results.Add(CheckSecureDns());
        results.Add(CheckHostsForYouTube());
        results.Add(CheckConflictingBypasses());
        await Task.CompletedTask;
        return results;
    }

    private static DiagnosticResult CheckBfe()
    {
        var s = ServiceManager.QueryService("BFE");
        return new DiagnosticResult
        {
            Name = "Base Filtering Engine",
            Level = s == ServiceControllerStatus.Running ? DiagnosticLevel.Ok : DiagnosticLevel.Error,
            Message = s == ServiceControllerStatus.Running ? "Запущен" : "Не запущен — zapret не сможет работать",
        };
    }

    private static DiagnosticResult CheckProxy()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Internet Settings");
            int enabled = (int)(key?.GetValue("ProxyEnable") ?? 0);
            string? server = key?.GetValue("ProxyServer") as string;

            if (enabled == 1)
                return new DiagnosticResult
                {
                    Name = "Системный прокси",
                    Level = DiagnosticLevel.Warning,
                    Message = $"Включён: {server}",
                    Hint = "Убедитесь что прокси валиден или отключите его",
                };

            return new DiagnosticResult { Name = "Системный прокси", Level = DiagnosticLevel.Ok, Message = "Отключён" };
        }
        catch (Exception ex)
        {
            return new DiagnosticResult { Name = "Системный прокси", Level = DiagnosticLevel.Info, Message = ex.Message };
        }
    }

    private static DiagnosticResult CheckTcpTimestamps()
    {
        try
        {
            var psi = new ProcessStartInfo("netsh", "interface tcp show global")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
            };
            using var p = Process.Start(psi)!;
            string output = p.StandardOutput.ReadToEnd();
            p.WaitForExit(3000);

            bool enabled = output.Split('\n').Any(line =>
                line.Contains("timestamps", StringComparison.OrdinalIgnoreCase) &&
                line.Contains("enabled", StringComparison.OrdinalIgnoreCase));

            if (enabled)
                return new DiagnosticResult { Name = "TCP timestamps", Level = DiagnosticLevel.Ok, Message = "Включены" };

            ServiceManager.EnableTcpTimestamps();
            return new DiagnosticResult { Name = "TCP timestamps", Level = DiagnosticLevel.Warning, Message = "Были выключены — попытались включить" };
        }
        catch (Exception ex)
        {
            return new DiagnosticResult { Name = "TCP timestamps", Level = DiagnosticLevel.Info, Message = ex.Message };
        }
    }

    private static DiagnosticResult CheckProcess(string processName, string warning, string? hint = null)
    {
        var procs = Process.GetProcessesByName(processName);
        if (procs.Length > 0)
            return new DiagnosticResult
            {
                Name = $"Процесс {processName}",
                Level = DiagnosticLevel.Error,
                Message = $"Найден ({procs.Length} шт.) — {warning}",
                Hint = hint,
            };

        return new DiagnosticResult { Name = $"Процесс {processName}", Level = DiagnosticLevel.Ok, Message = "Не найден" };
    }

    private static DiagnosticResult CheckServiceContains(string contains, string warning)
    {
        try
        {
            var services = ServiceController.GetServices();
            var found = services.Where(s => s.ServiceName.Contains(contains, StringComparison.OrdinalIgnoreCase) ||
                                            s.DisplayName.Contains(contains, StringComparison.OrdinalIgnoreCase)).ToList();
            if (found.Count > 0)
                return new DiagnosticResult
                {
                    Name = $"Сервис {contains}",
                    Level = DiagnosticLevel.Error,
                    Message = $"Найдено: {string.Join(", ", found.Select(s => s.ServiceName))}",
                    Hint = warning,
                };

            return new DiagnosticResult { Name = $"Сервис {contains}", Level = DiagnosticLevel.Ok, Message = "Не найден" };
        }
        catch (Exception ex)
        {
            return new DiagnosticResult { Name = $"Сервис {contains}", Level = DiagnosticLevel.Info, Message = ex.Message };
        }
    }

    private static DiagnosticResult CheckIntelConnectivity()
    {
        try
        {
            var services = ServiceController.GetServices();
            var match = services.Where(s =>
                (s.DisplayName.Contains("Intel", StringComparison.OrdinalIgnoreCase) ||
                 s.ServiceName.Contains("Intel", StringComparison.OrdinalIgnoreCase)) &&
                s.DisplayName.Contains("Connectivity", StringComparison.OrdinalIgnoreCase) &&
                s.DisplayName.Contains("Network", StringComparison.OrdinalIgnoreCase)).ToList();
            if (match.Count > 0)
                return new DiagnosticResult
                {
                    Name = "Intel Connectivity",
                    Level = DiagnosticLevel.Error,
                    Message = string.Join(", ", match.Select(s => s.ServiceName)),
                    Hint = "Intel Connectivity Network Service конфликтует с zapret",
                };

            return new DiagnosticResult { Name = "Intel Connectivity", Level = DiagnosticLevel.Ok, Message = "Не найден" };
        }
        catch (Exception ex)
        {
            return new DiagnosticResult { Name = "Intel Connectivity", Level = DiagnosticLevel.Info, Message = ex.Message };
        }
    }

    private static DiagnosticResult CheckCheckPoint()
    {
        var s1 = ServiceManager.QueryService("TracSrvWrapper");
        var s2 = ServiceManager.QueryService("EPWD");
        if (s1 is not null || s2 is not null)
            return new DiagnosticResult
            {
                Name = "Check Point",
                Level = DiagnosticLevel.Error,
                Message = "Найдены сервисы Check Point",
                Hint = "Попробуйте удалить Check Point",
            };

        return new DiagnosticResult { Name = "Check Point", Level = DiagnosticLevel.Ok, Message = "Не найден" };
    }

    private DiagnosticResult CheckWinDivertFile()
    {
        string sysPath = Path.Combine(_root, "bin", "WinDivert64.sys");
        return new DiagnosticResult
        {
            Name = "WinDivert64.sys",
            Level = File.Exists(sysPath) ? DiagnosticLevel.Ok : DiagnosticLevel.Error,
            Message = File.Exists(sysPath) ? "Найден" : "Отсутствует — переустановите zapret",
        };
    }

    private static DiagnosticResult CheckVpn()
    {
        try
        {
            var services = ServiceController.GetServices();
            var found = services.Where(s => s.DisplayName.Contains("VPN", StringComparison.OrdinalIgnoreCase) ||
                                            s.ServiceName.Contains("VPN", StringComparison.OrdinalIgnoreCase)).ToList();
            if (found.Count > 0)
                return new DiagnosticResult
                {
                    Name = "VPN-сервисы",
                    Level = DiagnosticLevel.Warning,
                    Message = string.Join(", ", found.Select(s => s.ServiceName)),
                    Hint = "Убедитесь, что VPN отключены",
                };

            return new DiagnosticResult { Name = "VPN-сервисы", Level = DiagnosticLevel.Ok, Message = "Не найдены" };
        }
        catch (Exception ex)
        {
            return new DiagnosticResult { Name = "VPN-сервисы", Level = DiagnosticLevel.Info, Message = ex.Message };
        }
    }

    private static DiagnosticResult CheckSecureDns()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"System\CurrentControlSet\Services\Dnscache\InterfaceSpecificParameters");
            if (key is null)
                return new DiagnosticResult { Name = "Безопасный DNS", Level = DiagnosticLevel.Warning, Message = "Не настроен", Hint = "Настройте DoH в браузере или в Windows 11" };

            bool found = false;
            foreach (var sub in key.GetSubKeyNames())
            {
                using var k = key.OpenSubKey(sub);
                if (k?.GetValue("DohFlags") is long flags && flags > 0) { found = true; break; }
                if (k?.GetValue("DohFlags") is int iflags && iflags > 0) { found = true; break; }
            }

            return new DiagnosticResult
            {
                Name = "Безопасный DNS",
                Level = found ? DiagnosticLevel.Ok : DiagnosticLevel.Warning,
                Message = found ? "Настроен (DoH)" : "Не обнаружен",
                Hint = found ? null : "Настройте DoH в браузере или в Windows 11",
            };
        }
        catch (Exception ex)
        {
            return new DiagnosticResult { Name = "Безопасный DNS", Level = DiagnosticLevel.Info, Message = ex.Message };
        }
    }

    private static DiagnosticResult CheckHostsForYouTube()
    {
        try
        {
            string hostsFile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "drivers", "etc", "hosts");
            if (!File.Exists(hostsFile))
                return new DiagnosticResult { Name = "Файл hosts", Level = DiagnosticLevel.Info, Message = "Не найден" };

            string content = File.ReadAllText(hostsFile);
            bool hasYt = content.Contains("youtube.com", StringComparison.OrdinalIgnoreCase) ||
                         content.Contains("youtu.be", StringComparison.OrdinalIgnoreCase);

            return new DiagnosticResult
            {
                Name = "Файл hosts",
                Level = hasYt ? DiagnosticLevel.Warning : DiagnosticLevel.Ok,
                Message = hasYt ? "Содержит записи для youtube.com — это может ломать доступ" : "OK",
            };
        }
        catch (Exception ex)
        {
            return new DiagnosticResult { Name = "Файл hosts", Level = DiagnosticLevel.Info, Message = ex.Message };
        }
    }

    private static DiagnosticResult CheckConflictingBypasses()
    {
        string[] names = ["GoodbyeDPI", "discordfix_zapret", "winws1", "winws2"];
        var found = names.Where(n => ServiceManager.QueryService(n) is not null).ToList();
        if (found.Count > 0)
            return new DiagnosticResult
            {
                Name = "Конкурирующие обходы",
                Level = DiagnosticLevel.Error,
                Message = string.Join(", ", found),
                Hint = "Удалите конкурирующие сервисы для корректной работы zapret",
            };

        return new DiagnosticResult { Name = "Конкурирующие обходы", Level = DiagnosticLevel.Ok, Message = "Не найдены" };
    }

    public string ClearDiscordCache()
    {
        try
        {
            foreach (var p in Process.GetProcessesByName("Discord"))
            {
                try { p.Kill(true); p.WaitForExit(2000); } catch { }
            }

            string cacheRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "discord");
            int deleted = 0;
            foreach (var sub in new[] { "Cache", "Code Cache", "GPUCache" })
            {
                string path = Path.Combine(cacheRoot, sub);
                if (Directory.Exists(path))
                {
                    try { Directory.Delete(path, true); deleted++; } catch { }
                }
            }
            return $"Кэш Discord очищен: удалено каталогов — {deleted}.";
        }
        catch (Exception ex)
        {
            return $"Ошибка очистки кэша: {ex.Message}";
        }
    }
}
