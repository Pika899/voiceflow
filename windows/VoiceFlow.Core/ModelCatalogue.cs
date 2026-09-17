namespace VoiceFlow.Core;

// Values copied verbatim from Sources/VoiceFlowCore/ModelManager.swift
// (knownModels), which documents them as verified on-machine with
// `shasum -a 256` against the actual downloaded files from
// huggingface.co/ggerganov/whisper.cpp.
public static class ModelCatalogue
{
    public static readonly IReadOnlyList<ModelInfo> KnownModels =
    [
        new ModelInfo(
            "base",
            "ggml-base.bin",
            new Uri("https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-base.bin"),
            "60ed5bc3dd14eea856493d334349b405782ddcaf0028d4b5df4088345fba2efe"),
        new ModelInfo(
            "small",
            "ggml-small.bin",
            new Uri("https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-small.bin"),
            "1be3a9b2063867b937e64e2ec7483364a79917e157fa98c5d94b5c1fffea987b"),
        new ModelInfo(
            "medium",
            "ggml-medium.bin",
            new Uri("https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-medium.bin"),
            "6c14d5adee5f86394037b4e4e8b59f1673b6cee10e3cf0b11bbdbee79c156208"),
    ];

    public static ModelInfo For(WhisperModelName name)
    {
        var key = name.ToString().ToLowerInvariant();
        return KnownModels.First(m => m.Name == key);
    }
}
