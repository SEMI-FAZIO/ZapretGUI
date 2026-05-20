using System.Diagnostics;
using System.Net.Http;
using System.Net.NetworkInformation;

namespace ZapretGUI.Services;

public enum ProbeStatus { Pending, Ok, Fail }

public sealed class ProbeResult
{
    public required string Host { get; init; }
    public required string Title { get; init; }
    public ProbeStatus Status { get; set; } = ProbeStatus.Pending;
    public long? HttpLatencyMs { get; set; }
    public long? PingMs { get; set; }
    public string? Detail { get; set; }
    public int? HttpStatus { get; set; }
}

public sealed class ConnectivityChecker
{
    private static readonly HttpClient _http = new(new HttpClientHandler { AllowAutoRedirect = false })
    {
        Timeout = TimeSpan.FromSeconds(8),
    };

    public IReadOnlyList<ProbeResult> Build() => new[]
    {
        new ProbeResult { Host = "discord.com", Title = "Discord" },
        new ProbeResult { Host = "youtube.com", Title = "YouTube" },
        new ProbeResult { Host = "google.com",  Title = "Google" },
        new ProbeResult { Host = "github.com",  Title = "GitHub" },
    };

    public async Task ProbeAsync(ProbeResult result, CancellationToken ct = default)
    {
        result.Status = ProbeStatus.Pending;
        try
        {
            var pingTask = MeasurePingAsync(result.Host, ct);
            var httpTask = MeasureHttpsAsync(result.Host, ct);

            result.PingMs = await pingTask;
            var (latency, status) = await httpTask;
            result.HttpLatencyMs = latency;
            result.HttpStatus = status;

            bool ok = status is >= 200 and < 500 && latency.HasValue;
            result.Status = ok ? ProbeStatus.Ok : ProbeStatus.Fail;
            result.Detail = status.HasValue ? $"HTTP {status}" : "no response";
        }
        catch (Exception ex)
        {
            result.Status = ProbeStatus.Fail;
            result.Detail = ex.GetBaseException().Message;
        }
    }

    private static async Task<long?> MeasurePingAsync(string host, CancellationToken ct)
    {
        try
        {
            using var p = new Ping();
            var reply = await p.SendPingAsync(host, 2000);
            if (reply.Status == IPStatus.Success) return reply.RoundtripTime;
        }
        catch { }
        return null;
    }

    private static async Task<(long? latency, int? status)> MeasureHttpsAsync(string host, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Head, $"https://{host}/");
            using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
            sw.Stop();
            return (sw.ElapsedMilliseconds, (int)resp.StatusCode);
        }
        catch
        {
            sw.Stop();
            return (null, null);
        }
    }
}
