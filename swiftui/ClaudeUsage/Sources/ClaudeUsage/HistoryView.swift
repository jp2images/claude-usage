import SwiftUI
import AppKit
import UniformTypeIdentifiers

/// The usage-history window. Counterpart to details.go.
struct HistoryView: View {
    @State private var stats: StatsCache?
    @State private var errorMessage: String?

    var body: some View {
        VStack(alignment: .leading, spacing: 0) {
            HStack {
                Text("Claude Usage History").font(.headline)
                Spacer()
                Button { exportCSV() } label: {
                    Label("Export CSV", systemImage: "square.and.arrow.down")
                }
                .disabled(stats == nil)
                Button { load() } label: {
                    Label("Refresh", systemImage: "arrow.clockwise")
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
        .frame(minWidth: 660, minHeight: 580)
        .onAppear(perform: load)
    }

    private func load() {
        do {
            stats = try StatsRepository.load()
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

    private static func dailyActivityCSV(_ stats: StatsCache) -> String {
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
            Button("Retry", action: load)
        }
        .frame(maxWidth: .infinity)
        .padding(.top, 24)
    }

    @ViewBuilder
    private func body(for stats: StatsCache) -> some View {
        let totals = Formatting.totalTokens(stats.modelUsage)
        let costStr = totals.costUSD > 0 ? String(format: "$%.4f", totals.costUSD) : "—"

        VStack(alignment: .leading, spacing: 10) {
            Card(title: "Overview") {
                KeyValueGrid(pairs: [
                    ("Messages", Formatting.number(stats.totalMessages)),
                    ("Sessions", Formatting.number(stats.totalSessions)),
                    ("Total tokens", Formatting.tokens(totals.inputTokens + totals.outputTokens)),
                    ("Est. cost", costStr),
                    ("Active since", Formatting.longDate(stats.firstSessionDate)),
                    ("Last updated", Formatting.date(stats.lastComputedDate)),
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
                Text("Longest session:").foregroundStyle(.secondary)
                Text(longestSession(stats))
            }
            .padding(.bottom, 8)
        }
    }

    private func cachePairs(_ totals: ModelUsage) -> [(String, String)] {
        var pairs = [
            ("Cache reads", Formatting.tokens(totals.cacheReadInputTokens)),
            ("Cache writes", Formatting.tokens(totals.cacheCreationInputTokens)),
        ]
        if totals.webSearchRequests > 0 {
            pairs.append(("Web searches", Formatting.number(totals.webSearchRequests)))
        }
        return pairs
    }

    private func longestSession(_ stats: StatsCache) -> String {
        guard stats.longestSession.messageCount > 0 else { return "—" }
        return "\(Formatting.number(stats.longestSession.messageCount)) messages, \(Formatting.duration(stats.longestSession.duration))"
    }

    @ViewBuilder
    private func modelTable(stats: StatsCache, totals: ModelUsage, costStr: String) -> some View {
        let models = stats.modelUsage.sorted { $0.key > $1.key }
        Grid(alignment: .leading, horizontalSpacing: 8, verticalSpacing: 4) {
            GridRow {
                Text("Model").bold().gridColumnAlignment(.leading)
                Text("Input").bold().gridColumnAlignment(.trailing)
                Text("Output").bold().gridColumnAlignment(.trailing)
                Text("Cache Reads").bold().gridColumnAlignment(.trailing)
                Text("Cost").bold().gridColumnAlignment(.trailing)
            }
            Divider()
            ForEach(models, id: \.key) { id, u in
                GridRow {
                    Text(Formatting.friendlyModel(id))
                    Text(Formatting.tokens(u.inputTokens))
                    Text(Formatting.tokens(u.outputTokens))
                    Text(Formatting.tokens(u.cacheReadInputTokens))
                    Text(u.costUSD > 0 ? String(format: "$%.4f", u.costUSD) : "—")
                }
            }
            Divider()
            GridRow {
                Text("Total").bold()
                Text(Formatting.tokens(totals.inputTokens)).bold()
                Text(Formatting.tokens(totals.outputTokens)).bold()
                Text(Formatting.tokens(totals.cacheReadInputTokens)).bold()
                Text(costStr).bold()
            }
        }
    }

    @ViewBuilder
    private func activityTable(_ activity: [DailyActivity]) -> some View {
        Grid(alignment: .leading, horizontalSpacing: 8, verticalSpacing: 4) {
            GridRow {
                Text("Date").bold().gridColumnAlignment(.leading)
                Text("Messages").bold().gridColumnAlignment(.trailing)
                Text("Sessions").bold().gridColumnAlignment(.trailing)
                Text("Tool Calls").bold().gridColumnAlignment(.trailing)
            }
            Divider()
            ForEach(activity.prefix(10), id: \.date) { day in
                GridRow {
                    Text(Formatting.date(day.date))
                    Text(Formatting.number(day.messageCount))
                    Text(Formatting.number(day.sessionCount))
                    Text(Formatting.number(day.toolCallCount))
                }
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
            Text(title).fontWeight(.bold)
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
