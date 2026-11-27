// swift-tools-version: 5.9
import PackageDescription

let package = Package(
    name: "OpenMemoryMac",
    platforms: [.macOS(.v13)],
    products: [.executable(name: "OpenMemory", targets: ["OpenMemory"])],
    targets: [
        .target(name: "OpenMemoryCore"),
        .executableTarget(name: "OpenMemory", dependencies: ["OpenMemoryCore"]),
        .testTarget(name: "OpenMemoryCoreTests", dependencies: ["OpenMemoryCore"])
    ]
)
