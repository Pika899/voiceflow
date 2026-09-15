import Foundation

/// What sits immediately before the insertion point in the target app.
public enum CursorContext: Equatable {
    /// The target app doesn't expose its text through Accessibility.
    case unavailable
    /// The cursor is at the very beginning of the text.
    case atStart
    case character(Character)
}

/// Decides whether a space must be inserted before a new transcription so
/// consecutive dictations don't run together ("siamo!Adesso").
public enum TextJoiner {
    public static func prefix(for text: String, precededBy context: CursorContext) -> String {
        guard let first = text.first, startsWord(first) else { return "" }
        switch context {
        case .unavailable, .atStart:
            return ""
        case .character(let previous):
            return needsSpace(after: previous) ? " " : ""
        }
    }

    private static func startsWord(_ c: Character) -> Bool {
        c.isLetter || c.isNumber
    }

    private static func needsSpace(after previous: Character) -> Bool {
        if previous.isWhitespace || previous.isNewline { return false }
        // Openers attach to what follows them.
        if "([{\"'«‘“".contains(previous) { return false }
        return true
    }
}
