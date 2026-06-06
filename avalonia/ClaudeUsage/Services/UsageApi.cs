using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using ClaudeUsage.Models;

namespace ClaudeUsage.Services;

/// Fetches live plan usage from the Claude.ai API using the Claude desktop
/// app's session cookies. Counterpart to api.go.
public sealed class UsageApi
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };

    public sealed record Result(PlanUsage Usage, RateLimits Limits);

    public static async Task<Result> LoadAsync()
    {
        // Cookie reading touches the filesystem, SQLite and DPAPI synchronously;
        // push it off the UI thread.
        var cookies = await Task.Run(ClaudeCookies.Read);

        var usage = await FetchAsync<PlanUsage>(
            $"https://claude.ai/api/organizations/{cookies.OrgId}/usage", cookies.SessionKey);
        var limits = await FetchAsync<RateLimits>(
            $"https://claude.ai/api/organizations/{cookies.OrgId}/rate_limits", cookies.SessionKey);

        return new Result(usage, limits);
    }

    private static async Task<T> FetchAsync<T>(string url, string sessionKey)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Add("Cookie", $"sessionKey={sessionKey}");
        req.Headers.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64)");
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var resp = await Http.SendAsync(req);
        if (resp.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            throw new InvalidOperationException("session expired — open the Claude desktop app to refresh");
        if (!resp.IsSuccessStatusCode)
            throw new InvalidOperationException($"HTTP {(int)resp.StatusCode}");

        var json = await resp.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<T>(json)
            ?? throw new InvalidOperationException("parsing response: empty result");
    }
}
