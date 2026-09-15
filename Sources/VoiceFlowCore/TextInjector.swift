import ApplicationServices
import CoreGraphics

public enum TextInjectionError: Error {
    case accessibilityNotTrusted
    /// Neither the Accessibility write nor synthetic keystrokes could deliver
    /// the text — the caller must surface this, never assume it was typed.
    case injectionFailed
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
        guard insertViaSyntheticKeystrokes(text) else {
            throw TextInjectionError.injectionFailed
        }
    }

    /// What precedes the insertion point in the focused element, read via
    /// Accessibility. `.unavailable` when the app exposes no text — the
    /// keystroke-fallback apps — so the caller can use its own memory instead.
    public func cursorContext() -> CursorContext {
        guard let element = focusedElement() else { return .unavailable }

        var rangeRef: AnyObject?
        guard AXUIElementCopyAttributeValue(element, kAXSelectedTextRangeAttribute as CFString, &rangeRef) == .success,
              let rangeRef,
              CFGetTypeID(rangeRef) == AXValueGetTypeID() else {
            return .unavailable
        }
        var range = CFRange()
        guard AXValueGetValue(rangeRef as! AXValue, .cfRange, &range) else { return .unavailable }
        guard range.location > 0 else { return .atStart }

        var previousRange = CFRange(location: range.location - 1, length: 1)
        guard let rangeValue = AXValueCreate(.cfRange, &previousRange) else { return .unavailable }
        var textRef: AnyObject?
        guard AXUIElementCopyParameterizedAttributeValue(
            element,
            kAXStringForRangeParameterizedAttribute as CFString,
            rangeValue,
            &textRef
        ) == .success, let previous = (textRef as? String)?.first else {
            return .unavailable
        }
        return .character(previous)
    }

    private func focusedElement() -> AXUIElement? {
        let systemWide = AXUIElementCreateSystemWide()
        var focusedElementRef: AnyObject?
        let copyResult = AXUIElementCopyAttributeValue(
            systemWide,
            kAXFocusedUIElementAttribute as CFString,
            &focusedElementRef
        )
        guard copyResult == .success,
              let focusedElementRef,
              CFGetTypeID(focusedElementRef) == AXUIElementGetTypeID() else {
            return nil
        }
        return (focusedElementRef as! AXUIElement)
    }

    /// Tries to write directly into the focused element's selected-text
    /// attribute. Works well for standard text fields; returns `false` for
    /// apps whose accessibility tree doesn't support this attribute, so the
    /// caller falls back to synthetic keystrokes.
    private func insertViaAccessibility(_ text: String) -> Bool {
        // focusedElement() checks the CF type before casting: a third-party
        // AX implementation can hand back the wrong type alongside .success.
        guard let focusedElement = focusedElement() else { return false }

        let setResult = AXUIElementSetAttributeValue(
            focusedElement,
            kAXSelectedTextAttribute as CFString,
            text as CFTypeRef
        )
        return setResult == .success
    }

    /// Fallback: synthesizes keyboard events carrying the Unicode text
    /// directly, bypassing the need for the target app to expose a
    /// settable accessibility attribute. Returns `false` if any event could
    /// not be created, so a dropped character is never silent.
    ///
    /// `virtualKey: 0` is kVK_ANSI_A; the Unicode payload is what Cocoa apps
    /// read. An app that inspects the key code instead of the characters
    /// would see "a" — a known limitation of this technique, acceptable for v1.
    private func insertViaSyntheticKeystrokes(_ text: String) -> Bool {
        guard let source = CGEventSource(stateID: .hidSystemState) else { return false }
        for scalar in text.unicodeScalars {
            let utf16 = Array(String(scalar).utf16)
            guard let keyDown = CGEvent(keyboardEventSource: source, virtualKey: 0, keyDown: true),
                  let keyUp = CGEvent(keyboardEventSource: source, virtualKey: 0, keyDown: false) else {
                return false
            }
            keyDown.keyboardSetUnicodeString(stringLength: utf16.count, unicodeString: utf16)
            keyUp.keyboardSetUnicodeString(stringLength: utf16.count, unicodeString: utf16)
            keyDown.post(tap: .cghidEventTap)
            keyUp.post(tap: .cghidEventTap)
        }
        return true
    }
}
