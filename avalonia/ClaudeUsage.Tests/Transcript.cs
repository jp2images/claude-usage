using System.Text.Json;
using ClaudeUsage.Models;
using ClaudeUsage.Services;

namespace ClaudeUsage.Tests;

/// Builds JSONL lines in the shape Claude Code writes and folds them into an
/// aggregate, so the tests exercise the same parse path the app uses.
internal static class Transcript
{
    internal static string AssistantRecord(
        string messageId,
        string requestId,
        string model,
        string timestamp,
        string sessionId,
        long outputTokens = 100,
        long cacheWrite1h = 0,
        int toolCalls = 0)
    {
        var content = new List<object> { new { type = "text" } };
        for (var i = 0; i < toolCalls; i++)
            content.Add(new { type = "tool_use" });

        // Serialized rather than written out as literal JSON so the record
        // stays compact and unspaced, which is what the reader's
        // "type":"assistant" prefilter looks for.
        return JsonSerializer.Serialize(new
        {
            type = "assistant",
            timestamp,
            sessionId,
            requestId,
            uuid = $"u-{messageId}-{requestId}",
            message = new
            {
                id = messageId,
                model,
                content,
                usage = new
                {
                    input_tokens = 10,
                    output_tokens = outputTokens,
                    cache_read_input_tokens = 100,
                    cache_creation_input_tokens = cacheWrite1h,
                    cache_creation = new
                    {
                        ephemeral_5m_input_tokens = 0,
                        ephemeral_1h_input_tokens = cacheWrite1h,
                    },
                },
            },
        });
    }

    internal static UsageStats Scan(params string[] lines)
    {
        var path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".jsonl");
        File.WriteAllLines(path, lines);
        try
        {
            return TranscriptReader.Aggregate(new[] { path });
        }
        finally
        {
            File.Delete(path);
        }
    }
}
