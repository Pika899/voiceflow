namespace VoiceFlow.Core;

/// <summary>
/// What sits immediately before the insertion point in the target app.
/// </summary>
public abstract record CursorContext
{
    /// <summary>The target app doesn't expose its text through Accessibility.</summary>
    public sealed record Unavailable : CursorContext;

    /// <summary>The cursor is at the very beginning of the text.</summary>
    public sealed record AtStart : CursorContext;

    public sealed record Character(char Value) : CursorContext;
}
