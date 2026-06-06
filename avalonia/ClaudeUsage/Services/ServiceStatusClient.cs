using System.Net.Http;
using System.Text.Json;

namespace ClaudeUsage.Services;

public sealed record ServiceStatus(string Indicator, string Description)
{
    public static readonly ServiceStatus Unknown = new("unknown", "Status unavailable");
}

/// Polls the public Statuspage summary at status.claude.com. Counterpart to status.go.
public static class ServiceStatusClient
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(5) };

    public static async Task<ServiceStatus> FetchAsync()
    {
        try
        {
            var json = await Http.GetStringAsync("https://status.claude.com/api/v2/status.json");
            using var doc = JsonDocument.Parse(json);
            var status = doc.RootElement.GetProperty("status");
            return new ServiceStatus(
                status.GetProperty("indicator").GetString() ?? "unknown",
                status.GetProperty("description").GetString() ?? "");
        }
        catch
        {
            return ServiceStatus.Unknown;
        }
    }
}
