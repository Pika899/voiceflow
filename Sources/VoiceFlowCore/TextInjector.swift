import ApplicationServices
import CoreGraphics

public enum TextInjectionError: Error {
    case accessibilityNotTrusted
}

public final class TextInjector {
    public init() {}

    public func inject(_ text: String) throws {
        guard AXIsProcessTrusted() else {
            throw TextInjectionError.accessibilityNotTrusted
        }
        if insertViaAccessibility(text) {
            return
        }
        insertViaSyntheticKeystrokes(text)
    }

    /// Tries to write directly into the focused element's selected-text
    /// attribute. Works well for standard text fields; returns `false` for
    /// apps whose accessibility tree doesn't support this attribute, so the
    /// caller falls back to synthetic keystrokes.
    private func insertViaAccessibility(_ text: String) -> Bool {
        let systemWide = AXUIElementCreateSystemWide()
        var focusedElementRef: AnyObject?
        let copyResult = AXUIElementCopyAttributeValue(
            systemWide,
            kAXFocusedUIElementAttribute as CFString,
            &focusedElementRef
        )
        guard copyResult == .success, let focusedElementRef else { return false }
        let focusedElement = focusedElementRef as! AXUIElement

        let setResult = AXUIElementSetAttributeValue(
            focusedElement,
            kAXSelectedTextAttribute as CFString,
            text as CFTypeRef
        )
        return setResult == .success
    }

    /// Fallback: synthesizes keyboard events carrying the Unicode text
    /// directly, bypassing the need for the target app to expose a
    /// settable accessibility attribute.
    private func insertViaSyntheticKeystrokes(_ text: String) {
        let source = CGEventSource(stateID: .hidSystemState)
        for scalar in text.unicodeScalars {
            let utf16 = Array(String(scalar).utf16)
            guard let keyDown = CGEvent(keyboardEventSource: source, virtualKey: 0, keyDown: true) else { continue }
            keyDown.keyboardSetUnicodeString(stringLength: utf16.count, unicodeString: utf16)
            keyDown.post(tap: .cghidEventTap)

            guard let keyUp = CGEvent(keyboardEventSource: source, virtualKey: 0, keyDown: false) else { continue }
            keyUp.keyboardSetUnicodeString(stringLength: utf16.count, unicodeString: utf16)
            keyUp.post(tap: .cghidEventTap)
        }
    }
}
