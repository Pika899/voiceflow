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
    dependencies: [
        .package(url: "https://github.com/soffes/HotKey", from: "0.2.1")
    ],
    targets: [
        .systemLibrary(name: "CWhisper", path: "Sources/CWhisper"),
        .target(
            name: "VoiceFlowCore",
            dependencies: [
                "CWhisper",
                .product(name: "HotKey", package: "HotKey")
            ],
            linkerSettings: [
                .unsafeFlags([
                    "-LVendor/whisper/lib",
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
