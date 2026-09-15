import AppKit
import Carbon.HIToolbox

/// Push-to-talk on the fn (🌐) key. fn is a modifier, not a key, so Carbon's
/// RegisterEventHotKey cannot bind it; instead we watch `.flagsChanged`
/// events. Global monitors need Accessibility, which the app already has
/// for text injection.
public final class FnKeyMonitor {
    public var onPress: (() -> Void)?
    public var onRelease: (() -> Void)?

    private var globalMonitor: Any?
    private var localMonitor: Any?
    private var isDown = false

    public init() {}

    deinit {
        stop()
    }

    /// Returns `false` when the process is not Accessibility-trusted: without
    /// that permission macOS silently delivers no global keyboard events.
    @discardableResult
    public func start() -> Bool {
        guard AXIsProcessTrusted() else { return false }
        guard globalMonitor == nil else { return true }

        let handler: (NSEvent) -> Void = { [weak self] event in self?.handle(event) }
        globalMonitor = NSEvent.addGlobalMonitorForEvents(matching: .flagsChanged, handler: handler)
        // Global monitors skip events aimed at our own app (e.g. while the
        // popover is open); the local monitor covers that case.
        localMonitor = NSEvent.addLocalMonitorForEvents(matching: .flagsChanged) { event in
            handler(event)
            return event
        }
        return true
    }

    public func stop() {
        if let globalMonitor {
            NSEvent.removeMonitor(globalMonitor)
        }
        if let localMonitor {
            NSEvent.removeMonitor(localMonitor)
        }
        globalMonitor = nil
        localMonitor = nil
        isDown = false
    }

    private func handle(_ event: NSEvent) {
        // `.flagsChanged` fires for every modifier, and the `.function` flag
        // also rides along with arrow/page keys — the key code is the clean
        // discriminator for the fn key itself.
        guard event.keyCode == UInt16(kVK_Function) else { return }
        let down = event.modifierFlags.contains(.function)
        guard down != isDown else { return }
        isDown = down
        if down {
            onPress?()
        } else {
            onRelease?()
        }
    }
}
