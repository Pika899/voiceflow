// swift-tools-version:5.9
import PackageDescription

let package = Package(
    name: "VoiceFlow",
    // macOS 14, not 13: the Testing.framework shipped with the Command Line
    // Tools is built for 14.0, and linking it into a 13.0 target warns.
    // v1 is local-only, so raising the floor costs nothing.
    platforms: [.macOS(.v14)],
    products: [
        .library(name: "VoiceFlowCore", targets: ["VoiceFlowCore"]),
        .executable(name: "LatencySpike", targets: ["LatencySpike"])
    ],
    dependencies: [],
    targets: [
        .systemLibrary(name: "CWhisper", path: "Sources/CWhisper"),
        .target(
            name: "VoiceFlowCore",
            dependencies: [
                "CWhisper"
            ],
            linkerSettings: [
                .unsafeFlags([
                    "-LVendor/whisper/lib",
                    // libwhisper.a/libggml.a are C++ static libraries and need
                    // the C++ runtime. With the (Swift-only) HotKey dependency
                    // present this was linked in transitively; removing that
                    // dependency (task 4) exposed the missing link and broke
                    // the LatencySpike executable, so it is now explicit.
                    "-lc++",
                    "-lwhisper",
                    "-lggml",
                    "-lggml-base",
                    "-lggml-cpu",
                    "-lggml-blas",
                    "-lggml-metal"
                ]),
                .linkedFramework("Accelerate"),
                .linkedFramework("Metal"),
                .linkedFramework("MetalKit"),
                .linkedFramework("Foundation")
            ]
        ),
        .executableTarget(
            name: "LatencySpike",
            dependencies: ["VoiceFlowCore"]
        ),
        .testTarget(
            name: "VoiceFlowCoreTests",
            dependencies: ["VoiceFlowCore"]
        )
    ]
)
