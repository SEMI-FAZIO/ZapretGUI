using System.Diagnostics.Eventing.Reader;

namespace ZapretGUI.Services;

/// <summary>
/// Reads recent Service Control Manager events for the zapret service from the System log.
/// Used to show service lifecycle (start, stop, crash) in the Logs page.
/// </summary>
public sealed class ServiceEventLog
{
    private readonly string _serviceName;

    public ServiceEventLog(string serviceName = ServiceManager.ZapretServiceName)
    {
        _serviceName = serviceName;
    }

    public IReadOnlyList<LogEntry> ReadRecent(int maxRecords = 50)
    {
        var entries = new List<LogEntry>();
        try
        {
            // Filter System log for Service Control Manager events whose EventData contains the service name.
            string xpath = $"*[System[Provider[@Name='Service Control Manager']]]";

            var q = new EventLogQuery("System", PathType.LogName, xpath)
            {
                ReverseDirection = true,
            };

            using var reader = new EventLogReader(q);
            EventRecord? rec;
            int taken = 0;
            while (taken < maxRecords && (rec = reader.ReadEvent()) is not null)
            {
                using (rec)
                {
                    if (!Matches(rec, _serviceName)) continue;

                    var level = rec.Id switch
                    {
                        7034 => LogLevel.Error,    // service terminated unexpectedly
                        7000 => LogLevel.Error,    // service failed to start
                        7031 => LogLevel.Warning,  // service terminated, will restart per recovery
                        7042 => LogLevel.Warning,  // service stop control sent
                        _ => LogLevel.Info,
                    };

                    string text = rec.FormatDescription() ?? $"Event {rec.Id}";
                    text = text.Replace("\r\n", " ").Replace('\n', ' ').Trim();
                    if (text.Length > 320) text = text[..320] + "…";

                    entries.Add(new LogEntry(
                        rec.TimeCreated ?? DateTime.Now,
                        level,
                        $"[SCM #{rec.Id}] {text}",
                        LogSource.Service));

                    taken++;
                }
            }
        }
        catch
        {
            // If the user doesn't have permission to read System log, fail silently.
        }
        return entries;
    }

    private static bool Matches(EventRecord rec, string serviceName)
    {
        try
        {
            foreach (var p in rec.Properties)
            {
                if (p.Value is string s && s.Equals(serviceName, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
        }
        catch { }
        return false;
    }
}
