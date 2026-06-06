import Foundation

/// Polls the public Statuspage summary at status.claude.com. Counterpart to status.go.
enum ServiceStatusClient {

    static func fetch() async -> ServiceStatus {
        guard let url = URL(string: "https://status.claude.com/api/v2/status.json") else {
            return .unknown
        }
        var request = URLRequest(url: url)
        request.timeoutInterval = 5

        do {
            let (data, _) = try await URLSession.shared.data(for: request)
            let payload = try JSONDecoder().decode(StatusPayload.self, from: data)
            return ServiceStatus(indicator: payload.status.indicator, description: payload.status.description)
        } catch {
            return .unknown
        }
    }

    private struct StatusPayload: Decodable {
        struct Status: Decodable {
            let indicator: String
            let description: String
        }
        let status: Status
    }
}
