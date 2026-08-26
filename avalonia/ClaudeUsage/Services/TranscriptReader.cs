using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using ClaudeUsage.Models;

namespace ClaudeUsage.Services;

/// Walks Claude Code's JSONL transcripts under ~/.claude/projects and
/// aggregates token usage, cost, and daily activity. Counterpart to
/// transcripts.go.
///
/// This replaces ~/.claude/stats-cache.json, which Claude Code no longer
/// maintains and whose costUSD field was always 0. The transcripts also carry
/// per-message timestamps and cache-TTL detail the cache file never held.
public static class TranscriptReader
{
    /// Claude Code writes one JSONL transcript per session. Only assistant
    /// records carry a usage payload, and they are the only lines this reader
    /// parses.
    private const string AssistantMarker = "\"type\":\"assistant\"";

    /// The placeholder Claude Code records for messages it generates locally.
    /// They have no tokens and no cost, so they are skipped.
    private const string SyntheticModel = "<synthetic>";

    /// The pause after which a session is treated as picked up again rather
    /// than still running. A resumed session can span days of wall clock, so
    /// its first-to-last extent says nothing about how long it was worked on.
    private static readonly TimeSpan IdleGap = TimeSpan.FromMinutes(30);

    public static string DefaultRoot => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", "projects");

    /// Aggregates the transcripts under root, defaulting to ~/.claude/projects.
    /// The root is injectable so tests can point it at a fixture directory.
    public static UsageStats Load(string? root = null)
    {
        root ??= DefaultRoot;

        var paths = FindTranscripts(root);
        if (paths.Count == 0)
            throw new FileNotFoundException(
                $"No usage transcripts found under {root}\n\n" +
                "Make sure Claude Code is installed and has been used at least once.");

        var fingerprint = TranscriptStatsCache.Fingerprint(paths);
        if (fingerprint is not null && TranscriptStatsCache.Read(fingerprint) is { } cached)
            return cached;

        var stats = Aggregate(paths);

        if (fingerprint is not null)
            TranscriptStatsCache.Write(fingerprint, stats);
        return stats;
    }

    /// Folds a set of transcripts into one aggregate, bypassing the cache.
    internal static UsageStats Aggregate(IReadOnlyList<string> paths)
    {
        var acc = new Accumulator();
        foreach (var path in paths)
            Scan(path, acc);
        return acc.Result();
    }

    internal static List<string> FindTranscripts(string root)
    {
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            // An unreadable subdirectory shouldn't abort the whole walk.
            IgnoreInaccessible = true,
        };

        List<string> paths;
        try
        {
            paths = Directory.EnumerateFiles(root, "*.jsonl", options).ToList();
        }
        catch (Exception e) when (e is DirectoryNotFoundException or UnauthorizedAccessException)
        {
            return new List<string>();
        }

        // Sorted so the walk order — and so the aggregate — is deterministic.
        paths.Sort(StringComparer.Ordinal);
        return paths;
    }

    /// Folds one file's assistant records into the accumulator.
    internal static void Scan(string path, Accumulator acc)
    {
        using var reader = new StreamReader(path);
        while (reader.ReadLine() is { } line)
        {
            // Most lines are user turns, attachments, or bookkeeping. A
            // substring test skips them without paying for a JSON parse.
            if (!line.Contains(AssistantMarker, StringComparison.Ordinal))
                continue;

            TranscriptRecord? rec;
            try
            {
                rec = JsonSerializer.Deserialize<TranscriptRecord>(line);
            }
            catch (JsonException)
            {
                // A malformed line shouldn't discard the rest of the session.
                continue;
            }

            if (rec is null) continue;
            rec.Message ??= new MessageDto();
            if (string.IsNullOrEmpty(rec.Message.Id) && string.IsNullOrEmpty(rec.Uuid)) continue;

            acc.Add(rec);
        }
    }

    // ── Accumulation ────────────────────────────────────────────────────────────

    /// One session, tracked so the busiest can be reported. Active accumulates
    /// only the gaps shorter than IdleGap.
    private sealed class SessionSpan
    {
        public DateTimeOffset First;
        public DateTimeOffset Last;
        public TimeSpan Active;
        public int Messages;
    }

    /// One calendar day of activity. Sessions are held as a set because a
    /// session's messages are spread across the day's records.
    private sealed class DayTotals
    {
        public int Messages;
        public int ToolCalls;
        public readonly HashSet<string> Sessions = new(StringComparer.Ordinal);
    }

    /// Collects every deduplicated record into the shape the history window
    /// renders.
    internal sealed class Accumulator
    {
        private readonly HashSet<string> _seen = new(StringComparer.Ordinal);
        private readonly Dictionary<string, ModelUsage> _models = new(StringComparer.Ordinal);
        private readonly Dictionary<string, DayTotals> _days = new(StringComparer.Ordinal);
        private readonly Dictionary<string, SessionSpan> _sessions = new(StringComparer.Ordinal);
        private DateTimeOffset? _first;
        private int _messages;

        /// Folds one record into the totals, ignoring records already seen.
        internal void Add(TranscriptRecord rec)
        {
            var message = rec.Message!;
            var sessionId = rec.SessionId ?? "";

            // Claude Code writes an assistant record per streamed content
            // block, so the same message recurs with an identical usage
            // payload. Deduplicate on (message id, request id) and keep the
            // first — counting every record roughly doubles every total.
            var id = string.IsNullOrEmpty(message.Id) ? rec.Uuid ?? "" : message.Id;
            if (!_seen.Add(id + "|" + rec.RequestId))
                return;

            var model = message.Model ?? "";
            if (model.Length == 0 || model == SyntheticModel)
                return;

            // Parsed before anything is counted: a record that can't be placed
            // in time is malformed, and gets the same treatment as malformed
            // JSON. Counting its tokens but not its day would leave the
            // overview's message count above the sum of the daily rows.
            if (!DateTimeOffset.TryParse(rec.Timestamp, CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind, out var parsed))
                return;
            var ts = parsed.ToLocalTime();

            if (!_models.TryGetValue(model, out var totals))
            {
                totals = new ModelUsage();
                _models[model] = totals;
            }

            var usage = message.Usage ?? new UsageDto();
            totals.InputTokens += usage.InputTokens ?? 0;
            totals.OutputTokens += usage.OutputTokens ?? 0;
            totals.CacheReadInputTokens += usage.CacheReadInputTokens ?? 0;
            totals.CacheCreationInputTokens += usage.CacheCreationInputTokens ?? 0;
            totals.WebSearchRequests += usage.ServerToolUse?.WebSearchRequests ?? 0;
            totals.MessageCount++;

            // Cache writes bill differently per TTL, so they are tracked
            // separately. Records predating the split report only a total;
            // attribute those to the 5-minute TTL, which is the default.
            var write5m = usage.CacheCreation?.Ephemeral5m ?? 0;
            var write1h = usage.CacheCreation?.Ephemeral1h ?? 0;
            if (write5m == 0 && write1h == 0)
                write5m = usage.CacheCreationInputTokens ?? 0;
            totals.CacheCreation5mTokens += write5m;
            totals.CacheCreation1hTokens += write1h;

            _messages++;

            var toolCalls = message.Content?.Count(b => b.Type == "tool_use") ?? 0;

            if (_first is null || ts < _first)
                _first = ts;

            var date = ts.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            if (!_days.TryGetValue(date, out var day))
            {
                day = new DayTotals();
                _days[date] = day;
            }
            day.Messages++;
            day.ToolCalls += toolCalls;

            if (sessionId.Length == 0) return;
            day.Sessions.Add(sessionId);

            if (!_sessions.TryGetValue(sessionId, out var span))
            {
                span = new SessionSpan { First = ts, Last = ts };
                _sessions[sessionId] = span;
            }
            if (ts < span.First)
                span.First = ts;
            // Records arrive in order within a session, so a forward step is
            // the time since the previous message. Anything longer than IdleGap
            // was the session sitting idle, not being worked on.
            if (ts > span.Last)
            {
                var gap = ts - span.Last;
                if (gap < IdleGap)
                    span.Active += gap;
                span.Last = ts;
            }
            span.Messages++;
        }

        /// Converts the accumulated maps into a UsageStats.
        internal UsageStats Result()
        {
            var stats = new UsageStats
            {
                TotalMessages = _messages,
                TotalSessions = _sessions.Count,
            };

            foreach (var (model, usage) in _models)
            {
                usage.CostKnown = Pricing.TryGetCost(model, usage, out var cost);
                usage.CostUSD = cost;
                stats.ModelUsage[model] = usage;
            }

            stats.DailyActivity = _days
                .Select(kv => new DailyActivity
                {
                    Date = kv.Key,
                    MessageCount = kv.Value.Messages,
                    SessionCount = kv.Value.Sessions.Count,
                    ToolCallCount = kv.Value.ToolCalls,
                })
                .OrderByDescending(d => d.Date, StringComparer.Ordinal)
                .ToList();

            // Rank by message count, not elapsed time: the session left open
            // longest is usually one that was idle, not one that did the most
            // work.
            foreach (var (id, span) in _sessions)
            {
                if (span.Messages <= stats.BusiestSession.MessageCount) continue;
                stats.BusiestSession = new SessionSummary
                {
                    SessionId = id,
                    ActiveMillis = (long)span.Active.TotalMilliseconds,
                    MessageCount = span.Messages,
                    Timestamp = Rfc3339(span.First),
                };
            }

            if (_first is { } first)
                stats.FirstSessionDate = Rfc3339(first);
            if (stats.DailyActivity.Count > 0)
                stats.LastActivityDate = stats.DailyActivity[0].Date;

            return stats;
        }

        private static string Rfc3339(DateTimeOffset t) =>
            t.ToString("yyyy-MM-ddTHH:mm:sszzz", CultureInfo.InvariantCulture);
    }

    // ── Transcript JSON ─────────────────────────────────────────────────────────
    //
    // The subset of an assistant record that usage aggregation needs. Every
    // field is nullable so a record that writes one as null still folds in as
    // zero or empty rather than failing the whole line.

    internal sealed class TranscriptRecord
    {
        [JsonPropertyName("timestamp")] public string? Timestamp { get; set; }
        [JsonPropertyName("sessionId")] public string? SessionId { get; set; }
        [JsonPropertyName("requestId")] public string? RequestId { get; set; }
        [JsonPropertyName("uuid")] public string? Uuid { get; set; }
        [JsonPropertyName("message")] public MessageDto? Message { get; set; }
    }

    internal sealed class MessageDto
    {
        [JsonPropertyName("id")] public string? Id { get; set; }
        [JsonPropertyName("model")] public string? Model { get; set; }
        [JsonPropertyName("content")] public List<ContentBlockDto>? Content { get; set; }
        [JsonPropertyName("usage")] public UsageDto? Usage { get; set; }
    }

    internal sealed class ContentBlockDto
    {
        [JsonPropertyName("type")] public string? Type { get; set; }
    }

    internal sealed class UsageDto
    {
        [JsonPropertyName("input_tokens")] public long? InputTokens { get; set; }
        [JsonPropertyName("output_tokens")] public long? OutputTokens { get; set; }
        [JsonPropertyName("cache_read_input_tokens")] public long? CacheReadInputTokens { get; set; }
        [JsonPropertyName("cache_creation_input_tokens")] public long? CacheCreationInputTokens { get; set; }
        [JsonPropertyName("cache_creation")] public CacheCreationDto? CacheCreation { get; set; }
        [JsonPropertyName("server_tool_use")] public ServerToolUseDto? ServerToolUse { get; set; }
    }

    internal sealed class CacheCreationDto
    {
        [JsonPropertyName("ephemeral_5m_input_tokens")] public long? Ephemeral5m { get; set; }
        [JsonPropertyName("ephemeral_1h_input_tokens")] public long? Ephemeral1h { get; set; }
    }

    internal sealed class ServerToolUseDto
    {
        [JsonPropertyName("web_search_requests")] public long? WebSearchRequests { get; set; }
    }
}
