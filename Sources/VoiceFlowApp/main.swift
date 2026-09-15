import AppKit
import VoiceFlowCore

// Plain AppKit entry point on purpose: a SwiftUI `App` needs at least one
// scene, and on macOS 26 a lone `Settings` scene is opened as a real window
// at launch — a blank "VoiceFlow Settings" window on top of a menu bar app.
// With no scenes there is nothing to open; SwiftUI is still used for the
// popover's content through NSHostingController.
final class AppDelegate: NSObject, NSApplicationDelegate {
    var statusBarController: StatusBarController?

    func applicationDidFinishLaunching(_ notification: Notification) {
        statusBarController = StatusBarController()
    }
}

let app = NSApplication.shared
let delegate = AppDelegate()
app.delegate = delegate
app.run()
