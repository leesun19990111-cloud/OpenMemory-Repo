import AppKit
import OpenMemoryCore

@main enum OpenMemoryMain {
    @MainActor static func main() throws {
        if CommandLine.arguments.contains("--smoke-test") {
            // No accessibility, automation, or login-item permission prompts on the build machine.
            let result = ["platform": "macOS", "version": "3.0.0-preview.1", "bundle": Bundle.main.bundleIdentifier ?? "unbundled", "status": "executable launched"]
            let data = try JSONSerialization.data(withJSONObject: result, options: [.sortedKeys])
            print(String(decoding: data, as: UTF8.self))
        } else if CommandLine.arguments.count == 3 && CommandLine.arguments[1] == "--render" {
            let app = NSApplication.shared; app.setActivationPolicy(.accessory)
            let delegate = AppDelegate()
            try delegate.renderPreview(to: URL(fileURLWithPath: CommandLine.arguments[2]))
        } else {
            let app = NSApplication.shared
            app.setActivationPolicy(.accessory)
            let delegate = AppDelegate()
            app.delegate = delegate
            app.run()
            withExtendedLifetime(delegate) {}
        }
    }
}
