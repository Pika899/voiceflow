using VoiceFlow.Core;

namespace VoiceFlow.Core.Tests;

public class TextJoinerTests
{
    [Fact]
    public void AddsSpaceBetweenSentenceAndWord()
    {
        Assert.Equal(" ", TextJoiner.Prefix("Adesso", new CursorContext.Character('!')));
        Assert.Equal(" ", TextJoiner.Prefix("Quando", new CursorContext.Character('a')));
        Assert.Equal(" ", TextJoiner.Prefix("Ciao", new CursorContext.Character('.')));
    }

    [Fact]
    public void NoSpaceAfterWhitespaceOrAtStart()
    {
        Assert.Equal("", TextJoiner.Prefix("Adesso", new CursorContext.Character(' ')));
        Assert.Equal("", TextJoiner.Prefix("Adesso", new CursorContext.Character('\n')));
        Assert.Equal("", TextJoiner.Prefix("Adesso", new CursorContext.AtStart()));
    }

    [Fact]
    public void NoSpaceWhenNextTextIsPunctuationOrEmpty()
    {
        Assert.Equal("", TextJoiner.Prefix(", però", new CursorContext.Character('o')));
        Assert.Equal("", TextJoiner.Prefix(".", new CursorContext.Character('o')));
        Assert.Equal("", TextJoiner.Prefix("", new CursorContext.Character('o')));
    }

    [Fact]
    public void NoSpaceAfterOpeningBracketOrQuote()
    {
        Assert.Equal("", TextJoiner.Prefix("ciao", new CursorContext.Character('(')));
        Assert.Equal("", TextJoiner.Prefix("ciao", new CursorContext.Character('"')));
        Assert.Equal("", TextJoiner.Prefix("ciao", new CursorContext.Character('«')));
    }

    [Fact]
    public void UnknownContextFallsBackToNothing()
    {
        Assert.Equal("", TextJoiner.Prefix("ciao", new CursorContext.Unavailable()));
    }

    [Fact]
    public void AccentedAndUppercaseStartsCountAsWords()
    {
        Assert.Equal(" ", TextJoiner.Prefix("È tardi", new CursorContext.Character('.')));
        Assert.Equal(" ", TextJoiner.Prefix("2024", new CursorContext.Character('l')));
    }
}
