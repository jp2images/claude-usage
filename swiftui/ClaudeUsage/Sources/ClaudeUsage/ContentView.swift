import SwiftUI

/// The compact dashboard. Counterpart to main.go's buildContent.
struct ContentView: View {
    @StateObject private var model = UsageModel()
    @Environment(\.openWindow) private var openWindow

    private let autoRefresh = Timer.publish(every: 60, on: .main, in: .common).autoconnect()

    var body: some View {
        VStack(alignment: .leading, spacing: 8) {
            header

            Divider()

            if let error = model.errorMessage {
                Label(error, systemImage: "exclamationmark.triangle")
                    .font(.callout)
                    .fixedSize(horizontal: false, vertical: true)
            } else if let usage = model.usage {
                content(usage)
            } else {
                ProgressView().frame(maxWidth: .infinity)
            }
        }
        .padding(12)
        .frame(width: 440)
        .task { await model.refresh() }
        .onReceive(autoRefresh) { _ in Task { await model.refresh() } }
    }

    private var header: some View {
        HStack {
            Text("Claude Usage").font(.headline)
            if let limits = model.limits {
                Text(Formatting.friendlyTier(limits.rateLimitTier)).foregroundStyle(.secondary)
            }
            Spacer()
            Circle()
                .fill(StatusColor.from(model.status.indicator))
                .frame(width: 9, height: 9)
                .help(model.status.description)
            Link("API Status", destination: URL(string: "https://status.claude.com")!)
                .font(.caption)
            Button { Task { await model.refresh() } } label: {
                Image(systemName: "arrow.clockwise")
            }
            .disabled(model.isLoading)
        }
    }

    @ViewBuilder
    private func content(_ usage: PlanUsage) -> some View {
        if let five = usage.fiveHour {
            BarRow(label: "Current Session",
                   subtext: Formatting.timeUntil(five.resetsAt),
                   percent: five.utilization,
                   boldLabel: true)
        } else {
            Text("No session data")
        }

        Divider()
        Text("Weekly Limits").font(.headline).fontWeight(.bold)

        ForEach(weeklyRows(usage), id: \.label) { row in
            BarRow(label: row.label,
                   subtext: Formatting.resetDay(row.period.resetsAt),
                   percent: row.period.utilization)
        }

        Divider()
        extraRow(usage.extraUsage)

        Divider()
        HStack {
            Spacer()
            Button("Usage History…") { openWindow(id: "history") }
            Spacer()
        }
    }

    private func weeklyRows(_ usage: PlanUsage) -> [(label: String, period: UsagePeriod)] {
        let candidates: [(String, UsagePeriod?)] = [
            ("All Models", usage.sevenDay),
            ("Sonnet", usage.sevenDaySonnet),
            ("Claude Design", usage.sevenDayOmelette),
            ("Opus", usage.sevenDayOpus),
        ]
        return candidates.compactMap { label, period in period.map { (label, $0) } }
    }

    @ViewBuilder
    private func extraRow(_ extra: ExtraUsage?) -> some View {
        let eu = extra ?? ExtraUsage(isEnabled: false)
        let percent = eu.utilization ?? 0
        let subtext: String = {
            if let util = eu.utilization {
                _ = util
                if let credits = eu.usedCredits { return String(format: "$%.2f spent", credits) }
                return ""
            }
            return eu.isEnabled ? "Enabled — no usage yet" : "Not enabled"
        }()
        BarRow(label: "Extra Usage", subtext: subtext, percent: percent)
    }
}

/// [ label + subtext | %used over a thin bar ] in two equal columns.
struct BarRow: View {
    let label: String
    var subtext: String = ""
    let percent: Double
    var boldLabel: Bool = false

    var body: some View {
        HStack(alignment: .center) {
            VStack(alignment: .leading, spacing: 1) {
                Text(label).fontWeight(boldLabel ? .bold : .regular)
                if !subtext.isEmpty {
                    Text(subtext).font(.caption).foregroundStyle(.secondary)
                }
            }
            .frame(maxWidth: .infinity, alignment: .leading)

            VStack(alignment: .trailing, spacing: 2) {
                Text("\(Int(percent))%").font(.caption).foregroundStyle(.secondary)
                ThinBar(value: percent / 100)
            }
            .frame(maxWidth: .infinity)
        }
        .padding(.vertical, 2)
    }
}

/// A 3pt rounded progress bar.
struct ThinBar: View {
    let value: Double

    var body: some View {
        GeometryReader { geo in
            ZStack(alignment: .leading) {
                Capsule().fill(Color.gray.opacity(0.25))
                Capsule().fill(Color.accentColor)
                    .frame(width: geo.size.width * min(max(value, 0), 1))
            }
        }
        .frame(height: 3)
    }
}

enum StatusColor {
    static func from(_ indicator: String) -> Color {
        switch indicator {
        case "none": return Color(red: 40/255, green: 167/255, blue: 69/255)
        case "minor": return Color(red: 255/255, green: 193/255, blue: 7/255)
        case "major": return Color(red: 253/255, green: 126/255, blue: 20/255)
        case "critical": return Color(red: 220/255, green: 53/255, blue: 69/255)
        default: return Color(red: 108/255, green: 117/255, blue: 125/255)
        }
    }
}
