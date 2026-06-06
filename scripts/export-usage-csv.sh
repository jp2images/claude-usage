#!/usr/bin/env bash
#
# Export Claude usage data to CSV files for analysis in Excel/Sheets.
#
# Produces, in the output directory (default ./exports):
#   cache_daily_activity.csv   - the stats-cache.json "Recent Activity" table
#   cache_model_totals.csv     - the stats-cache.json per-model totals
#   daily_by_model.csv         - COMPLETE current usage aggregated per day per
#                                model, parsed from the session transcripts
#                                (~/.claude/projects/**/*.jsonl). This is the one
#                                to use for trends; the cache is sparse/stale.
#
# Usage:  scripts/export-usage-csv.sh [output_dir]
# Requires: jq

set -euo pipefail

OUT_DIR="${1:-./exports}"
CLAUDE_DIR="${HOME}/.claude"
STATS="${CLAUDE_DIR}/stats-cache.json"
PROJECTS="${CLAUDE_DIR}/projects"

command -v jq >/dev/null || { echo "error: jq is required (brew install jq)"; exit 1; }
mkdir -p "$OUT_DIR"

# ── 1. stats-cache: daily activity ────────────────────────────────────────────
if [[ -f "$STATS" ]]; then
  {
    echo "date,messages,sessions,tool_calls"
    jq -r '.dailyActivity | sort_by(.date)[]
           | [.date, .messageCount, .sessionCount, .toolCallCount] | @csv' "$STATS"
  } > "$OUT_DIR/cache_daily_activity.csv"

  # ── 2. stats-cache: per-model totals ────────────────────────────────────────
  {
    echo "model,input_tokens,output_tokens,cache_read_tokens,cache_creation_tokens,web_search_requests,cost_usd"
    jq -r '.modelUsage | to_entries[]
           | [.key, .value.inputTokens, .value.outputTokens,
              .value.cacheReadInputTokens, .value.cacheCreationInputTokens,
              .value.webSearchRequests, .value.costUSD] | @csv' "$STATS"
  } > "$OUT_DIR/cache_model_totals.csv"
  echo "wrote cache_daily_activity.csv, cache_model_totals.csv"
else
  echo "note: $STATS not found — skipping cache exports"
fi

# ── 3. transcripts: complete daily usage per model ────────────────────────────
if [[ -d "$PROJECTS" ]]; then
  # 'agent' splits main session work from subagent (Task tool) work via
  # isSidechain. 'thinking_blocks' counts extended-thinking blocks as a rough
  # effort proxy — Claude Code does NOT record the configured effort level.
  {
    echo "date,model,agent,messages,input_tokens,output_tokens,cache_read_tokens,cache_creation_tokens,web_search_requests,thinking_blocks"
    find "$PROJECTS" -name '*.jsonl' -print0 \
      | xargs -0 cat \
      | jq -c 'select(.type=="assistant" and (.message.usage != null))
               | {date: (.timestamp[0:10]),
                  model: (.message.model // "unknown"),
                  agent: (if (.isSidechain // false) then "subagent" else "main" end),
                  thinking: ((.message.content // []) | map(select(.type=="thinking")) | length),
                  u: .message.usage}' \
      | jq -s -r '
          group_by(.date + "|" + .model + "|" + .agent)
          | map({
              date: .[0].date,
              model: .[0].model,
              agent: .[0].agent,
              messages: length,
              input: (map(.u.input_tokens // 0) | add),
              output: (map(.u.output_tokens // 0) | add),
              cache_read: (map(.u.cache_read_input_tokens // 0) | add),
              cache_creation: (map(.u.cache_creation_input_tokens // 0) | add),
              web_search: (map(.u.server_tool_use.web_search_requests // 0) | add),
              thinking: (map(.thinking) | add)
            })
          | sort_by(.date, .model, .agent)[]
          | [.date, .model, .agent, .messages, .input, .output, .cache_read, .cache_creation, .web_search, .thinking]
          | @csv'
  } > "$OUT_DIR/daily_by_model.csv"
  echo "wrote daily_by_model.csv ($(( $(wc -l < "$OUT_DIR/daily_by_model.csv") - 1 )) rows)"
else
  echo "note: $PROJECTS not found — skipping transcript export"
fi

echo "done -> $OUT_DIR"
