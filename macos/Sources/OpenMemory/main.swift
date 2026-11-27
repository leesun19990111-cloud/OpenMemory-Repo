import AppKit
import OpenMemoryCore

if CommandLine.arguments.contains("--smoke-test") {
    // Does not prompt for accessibility, automation, or login-item permissions on the build machine.
    let result = ["platform": "macOS", "version": "3.0.0-preview.1", "bundle": Bundle.main.bundleIdentifier ?? "unbundled", "status": "executable launched"]
    let data = try JSONSerialization.data(withJSONObject: result, options: [.sortedKeys])
    print(String(decoding: data, as: UTF8.self))
} else {
    let app = NSApplication.shared
    app.setActivationPolicy(.accessory)
    let delegate = AppDelegate()
    app.delegate = delegate
    app.run()
    withExtendedLifetime(delegate) {}
}
