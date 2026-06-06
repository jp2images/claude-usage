import SwiftUI

@main
struct ClaudeUsageApp: App {
    @NSApplicationDelegateAdaptor(AppDelegate.self) private var appDelegate

    var body: some Scene {
        WindowGroup("Claude Usage") {
            ContentView()
        }
        .windowResizability(.contentSize)

        Window("Claude Usage — History", id: "history") {
            HistoryView()
        }
    }
}

/// Running as a SwiftUI SPM executable, the process defaults to a background
/// activation policy; promote it so the window shows and takes focus.
final class AppDelegate: NSObject, NSApplicationDelegate {
    func applicationDidFinishLaunching(_ notification: Notification) {
        NSApplication.shared.setActivationPolicy(.regular)
        NSApplication.shared.activate(ignoringOtherApps: true)
    }

    func applicationShouldTerminateAfterLastWindowClosed(_ sender: NSApplication) -> Bool {
        true
    }
}
