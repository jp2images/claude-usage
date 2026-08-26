#!/usr/bin/env bash
#
# Export Claude usage data to CSV for analysis in Excel/Sheets.
#
# Produces, in the output directory (default ./exports):
#   daily_by_model.csv  - usage per day per model, parsed from the session
#                         transcripts (~/.claude/projects/**/*.jsonl)
#
# Usage:  scripts/export-usage-csv.sh [output_dir]
# Requires: jq

set -euo pipefail

OUT_DIR="${1:-./exports}"
CLAUDE_DIR="${HOME}/.claude"
PROJECTS="${CLAUDE_DIR}/projects"

command -v jq >/dev/null || { echo "error: jq is required (brew install jq)"; exit 1; }
mkdir -p "$OUT_DIR"

if [[ ! -d "$PROJECTS" ]]; then
  echo "error: $PROJECTS not found — is Claude Code installed?" >&2
  exit 1
fi

# 'agent' splits main session work from subagent (Task tool) work via
# isSidechain. 'thinking_blocks' counts extended-thinking blocks as a rough
# effort proxy — Claude Code does NOT record the configured effort level.
#
# Records are deduplicated on (message id, request id) before anything is
# summed. Claude Code writes one assistant record per streamed content block,
# so a message recurs several times carrying the same usage payload; counting
# every copy inflates the totals by roughly 2x.
{
  echo "date,model,agent,messages,input_tokens,output_tokens,cache_read_tokens,cache_creation_tokens,web_search_requests,thinking_blocks"
  find "$PROJECTS" -name '*.jsonl' -print0 \
    | xargs -0 cat \
    | jq -c 'select(.type=="assistant" and (.message.usage != null))
             | {key: "\(.message.id // .uuid)|\(.requestId // "")",
                date: (.timestamp | sub("\\.[0-9]+"; "") | fromdateiso8601 | strflocaltime("%Y-%m-%d")),
                model: (.message.model // "unknown"),
                agent: (if (.isSidechain // false) then "subagent" else "main" end),
                thinking: ((.message.content // []) | map(select(.type=="thinking")) | length),
                u: .message.usage}' \
    | jq -s -r '
        unique_by(.key)
        | map(select(.model != "<synthetic>"))
        | group_by(.date + "|" + .model + "|" + .agent)
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
echo "done -> $OUT_DIR"
