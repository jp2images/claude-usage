import SwiftUI
import AppKit
import UniformTypeIdentifiers

/// The usage-history window. Counterpart to details.go.
struct HistoryView: View {
    @State private var stats: UsageStats?
    @State private var errorMessage: String?
    @Environment(\.dismiss) private var dismiss

    /// The most recent active days, not the last N calendar days: gaps are
    /// skipped, so this can reach further back than a week.
    private static let recentActivityDays = 7

    var body: some View {
        VStack(alignment: .leading, spacing: 0) {
            HStack {
                Text("Claude Usage History").font(.headline)
                Spacer()
                Button { exportCSV() } label: {
                    Label("Export CSV", systemImage: "square.and.arrow.down")
                }
                .disabled(stats == nil)
                Button { Task { await load() } } label: {
                    Label("Refresh", systemImage: "arrow.clockwise")
                }
                Button { dismiss() } label: {
                    Label("Close", systemImage: "xmark")
                }
            }
            Divider().padding(.vertical, 8)

            ScrollView {
                if let error = errorMessage {
                    errorState(error)
                } else if let stats {
                    body(for: stats)
                } else {
                    ProgressView().frame(maxWidth: .infinity).padding(.top, 40)
                }
            }
        }
        .padding(12)
        .frame(minWidth: 780, minHeight: 640)
        .task { await load() }
        .onExitCommand { dismiss() }
    }

    /// Walking the transcripts takes seconds on a large corpus, so it runs off
    /// the main actor.
    private func load() async {
        do {
            stats = try await Task.detached(priority: .userInitiated) {
                try TranscriptReader.load()
            }.value
            errorMessage = nil
        } catch {
            errorMessage = error.localizedDescription
        }
    }

    /// Exports the dated activity table shown here. (For complete historical
    /// trends across all sessions, use scripts/export-usage-csv.sh.)
    private func exportCSV() {
        guard let stats else { return }
        let panel = NSSavePanel()
        panel.nameFieldStringValue = "claude-usage-daily-activity.csv"
        panel.allowedContentTypes = [.commaSeparatedText]
        panel.begin { response in
            guard response == .OK, let url = panel.url else { return }
            let csv = Self.dailyActivityCSV(stats)
            try? csv.data(using: .utf8)?.write(to: url)
        }
    }

    private static func dailyActivityCSV(_ stats: UsageStats) -> String {
        var lines = ["date,messages,sessions,tool_calls"]
        for d in stats.dailyActivity.sorted(by: { $0.date < $1.date }) {
            lines.append("\(d.date),\(d.messageCount),\(d.sessionCount),\(d.toolCallCount)")
        }
        return lines.joined(separator: "\n") + "\n"
    }

    @ViewBuilder
    private func errorState(_ message: String) -> some View {
        VStack(spacing: 8) {
            Text("Unable to load usage stats").fontWeight(.bold)
            Text(message).multilineTextAlignment(.center).foregroundStyle(.secondary)
            Button("Retry") { Task { await load() } }
        }
        .frame(maxWidth: .infinity)
        .padding(.top, 24)
    }

    @ViewBuilder
    private func body(for stats: UsageStats) -> some View {
        let totals = Formatting.totalTokens(stats.modelUsage)
        let costStr = Formatting.cost(totals.costUSD)

        VStack(alignment: .leading, spacing: 10) {
            Card(title: "Overview") {
                KeyValueGrid(pairs: [
                    ("Messages", Formatting.number(stats.totalMessages)),
                    ("Sessions", Formatting.number(stats.totalSessions)),
                    ("Total tokens", Formatting.tokens(totals.inputTokens + totals.outputTokens)),
                    ("Est. cost", costStr),
                    ("Active since", Formatting.longDate(stats.firstSessionDate)),
                    ("Last activity", Formatting.date(stats.lastActivityDate)),
                ])
            }

            Card(title: "Token Usage by Model") {
                modelTable(stats: stats, totals: totals, costStr: costStr)
            }

            Card(title: "Cache & Tools") {
                KeyValueGrid(pairs: cachePairs(totals))
            }

            Card(title: "Recent Activity") {
                activityTable(stats.dailyActivity)
            }

            HStack(spacing: 4) {
                Text("Busiest session:").foregroundStyle(.secondary)
                Text(busiestSession(stats))
            }
            .padding(.bottom, 8)
        }
    }

    private func cachePairs(_ totals: ModelUsage) -> [(String, String)] {
        // The TTL split is shown because it drives cost: a cache write bills at
        // 2x the input rate on the 1-hour TTL against 1.25x on the 5-minute one.
        var pairs = [
            ("Cache reads", Formatting.tokens(totals.cacheReadInputTokens)),
            ("Cache writes", Formatting.tokens(totals.cacheCreationInputTokens)),
            ("   5-min TTL", Formatting.tokens(totals.cacheCreation5mTokens)),
            ("   1-hour TTL", Formatting.tokens(totals.cacheCreation1hTokens)),
        ]
        if totals.webSearchRequests > 0 {
            pairs.append(("Web searches", Formatting.number(totals.webSearchRequests)))
        }
        return pairs
    }

    private func busiestSession(_ stats: UsageStats) -> String {
        guard stats.busiestSession.messageCount > 0 else { return "—" }
        return "\(Formatting.number(stats.busiestSession.messageCount)) messages, \(Formatting.duration(stats.busiestSession.activeMillis)) active"
    }

    @ViewBuilder
    private func modelTable(stats: UsageStats, totals: ModelUsage, costStr: String) -> some View {
        let models = stats.modelUsage.sorted { $0.key > $1.key }
        DataTable {
            GridRow {
                TableCell("Model").bold()
                TableCell("Input", alignment: .trailing).bold()
                TableCell("Output", alignment: .trailing).bold()
                TableCell("Cache Reads", alignment: .trailing).bold()
                TableCell("Cost", alignment: .trailing).bold()
            }
            Divider()
            // Cost stays bold on every row, not just the total: it is the
            // column that gets scanned. It also stays uncoloured, since a
            // green or red figure would read as a verdict on the amount.
            ForEach(Array(models.enumerated()), id: \.element.key) { index, entry in
                let (id, u) = entry
                GridRow {
                    TableCell(Formatting.friendlyModel(id))
                        .foregroundStyle(Palette.model(for: id))
                    TableCell(Formatting.tokens(u.inputTokens), alignment: .trailing)
                    TableCell(Formatting.tokens(u.outputTokens), alignment: .trailing)
                    TableCell(Formatting.tokens(u.cacheReadInputTokens), alignment: .trailing)
                    TableCell(Formatting.cost(u.costUSD), alignment: .trailing).bold()
                }
                .background(Palette.stripe(row: index))
            }
            Divider()
            GridRow {
                TableCell("Total").bold()
                TableCell(Formatting.tokens(totals.inputTokens), alignment: .trailing).bold()
                TableCell(Formatting.tokens(totals.outputTokens), alignment: .trailing).bold()
                TableCell(Formatting.tokens(totals.cacheReadInputTokens), alignment: .trailing).bold()
                TableCell(costStr, alignment: .trailing).bold()
            }
            .background(Palette.totalsTint)
        }
    }

    @ViewBuilder
    private func activityTable(_ activity: [DailyActivity]) -> some View {
        DataTable {
            GridRow {
                TableCell("Date").bold()
                TableCell("Messages", alignment: .trailing).bold()
                TableCell("Sessions", alignment: .trailing).bold()
                TableCell("Tool Calls", alignment: .trailing).bold()
            }
            Divider()
            ForEach(Array(activity.prefix(Self.recentActivityDays).enumerated()), id: \.element.date) { index, day in
                GridRow {
                    TableCell(Formatting.date(day.date))
                    TableCell(Formatting.number(day.messageCount), alignment: .trailing)
                    TableCell(Formatting.number(day.sessionCount), alignment: .trailing)
                    TableCell(Formatting.number(day.toolCallCount), alignment: .trailing)
                }
                .background(Palette.stripe(row: index))
            }
        }
    }
}

/// A bordered titled card.
struct Card<Content: View>: View {
    let title: String
    @ViewBuilder let content: () -> Content

    var body: some View {
        VStack(alignment: .leading, spacing: 8) {
            Text(title).fontWeight(.bold).foregroundStyle(Palette.accent)
            content()
        }
        .frame(maxWidth: .infinity, alignment: .leading)
        .padding(12)
        .overlay(
            RoundedRectangle(cornerRadius: 6)
                .stroke(Color.gray.opacity(0.25), lineWidth: 1)
        )
    }
}

/// A striped data grid. The grid itself carries no spacing — the padding lives
/// on the cells instead, so a row's background runs edge to edge with no gaps
/// between the columns.
struct DataTable<Content: View>: View {
    @ViewBuilder let content: () -> Content

    var body: some View {
        Grid(alignment: .leading, horizontalSpacing: 0, verticalSpacing: 0) {
            content()
        }
        .frame(maxWidth: .infinity)
    }
}

/// One cell of a `DataTable`. It fills its column so the row stripe behind it
/// covers the column's full width, not just the width of the text.
struct TableCell: View {
    let text: String
    var alignment: Alignment = .leading

    init(_ text: String, alignment: Alignment = .leading) {
        self.text = text
        self.alignment = alignment
    }

    var body: some View {
        Text(text)
            .frame(maxWidth: .infinity, alignment: alignment)
            .padding(.horizontal, 6)
            .padding(.vertical, 2)
    }
}

/// Two-column [muted label | bold value] grid.
struct KeyValueGrid: View {
    let pairs: [(String, String)]

    var body: some View {
        Grid(alignment: .leading, horizontalSpacing: 16, verticalSpacing: 4) {
            ForEach(Array(pairs.enumerated()), id: \.offset) { _, pair in
                GridRow {
                    Text(pair.0).foregroundStyle(.secondary)
                    Text(pair.1).fontWeight(.bold)
                }
            }
        }
    }
}
