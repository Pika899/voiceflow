import Testing
@testable import VoiceFlowCore

@Suite struct TextJoinerTests {
    @Test func addsSpaceBetweenSentenceAndWord() {
        #expect(TextJoiner.prefix(for: "Adesso", precededBy: .character("!")) == " ")
        #expect(TextJoiner.prefix(for: "Quando", precededBy: .character("a")) == " ")
        #expect(TextJoiner.prefix(for: "Ciao", precededBy: .character(".")) == " ")
    }

    @Test func noSpaceAfterWhitespaceOrAtStart() {
        #expect(TextJoiner.prefix(for: "Adesso", precededBy: .character(" ")) == "")
        #expect(TextJoiner.prefix(for: "Adesso", precededBy: .character("\n")) == "")
        #expect(TextJoiner.prefix(for: "Adesso", precededBy: .atStart) == "")
    }

    @Test func noSpaceWhenNextTextIsPunctuationOrEmpty() {
        #expect(TextJoiner.prefix(for: ", però", precededBy: .character("o")) == "")
        #expect(TextJoiner.prefix(for: ".", precededBy: .character("o")) == "")
        #expect(TextJoiner.prefix(for: "", precededBy: .character("o")) == "")
    }

    @Test func noSpaceAfterOpeningBracketOrQuote() {
        #expect(TextJoiner.prefix(for: "ciao", precededBy: .character("(")) == "")
        #expect(TextJoiner.prefix(for: "ciao", precededBy: .character("\"")) == "")
        #expect(TextJoiner.prefix(for: "ciao", precededBy: .character("«")) == "")
    }

    @Test func unknownContextFallsBackToNothing() {
        #expect(TextJoiner.prefix(for: "ciao", precededBy: .unavailable) == "")
    }

    @Test func accentedAndUppercaseStartsCountAsWords() {
        #expect(TextJoiner.prefix(for: "È tardi", precededBy: .character(".")) == " ")
        #expect(TextJoiner.prefix(for: "2024", precededBy: .character("l")) == " ")
    }
}
