using System.Text;

namespace VoiceFlow.Core;

/// <summary>
/// Decides whether a space must be inserted before a new transcription so
/// consecutive dictations don't run together ("siamo!Adesso").
/// </summary>
public static class TextJoiner
{
    // Openers attach to what follows them; the straight apostrophe counts too, since it also opens an elided word in Italian ("l'amico").
    private const string Openers = "([{\"'«‘“";

    public static string Prefix(string text, CursorContext precededBy)
    {
        if (string.IsNullOrEmpty(text) || !StartsWord(text))
        {
            return "";
        }

        return precededBy switch
        {
            CursorContext.Character character => NeedsSpace(character.Value) ? " " : "",
            _ => "",
        };
    }

    private static bool StartsWord(string text)
    {
        var rune = Rune.GetRuneAt(text, 0);
        return Rune.IsLetterOrDigit(rune);
    }

    private static bool NeedsSpace(char previous)
    {
        if (char.IsWhiteSpace(previous))
        {
            return false;
        }

        return !Openers.Contains(previous);
    }
}
