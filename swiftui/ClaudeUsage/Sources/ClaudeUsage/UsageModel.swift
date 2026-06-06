import SwiftUI

/// Observable state for the dashboard. Loads run off the main actor because the
/// loader functions are nonisolated `async` (cookie/SQLite work happens off-main).
@MainActor
final class UsageModel: ObservableObject {
    @Published var usage: PlanUsage?
    @Published var limits: RateLimits?
    @Published var status: ServiceStatus = .unknown
    @Published var errorMessage: String?
    @Published var isLoading = false

    func refresh() async {
        isLoading = true
        defer { isLoading = false }

        async let statusFetch = ServiceStatusClient.fetch()

        do {
            let result = try await UsageAPI.load()
            usage = result.usage
            limits = result.limits
            errorMessage = nil
        } catch {
            errorMessage = error.localizedDescription
        }

        status = await statusFetch
    }
}
