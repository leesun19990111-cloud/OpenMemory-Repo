import AppKit
import ApplicationServices
import OpenMemoryCore

struct AppInfo: Sendable {
    var pid: pid_t
    var bundleID: String
    var path: String
    var name: String
    var entry: Entry { Entry(kind: .app, bundleID: bundleID, appPath: path, appName: name, title: name, note: "앱 실행 · 확인 가능한 창 제목 보관") }
}

struct LiveWindow {
    var element: AXUIElement
    var record: WindowRecord
    var token: String
}

enum Accessibility {
    static func trusted(prompt: Bool = false) -> Bool {
        AXIsProcessTrustedWithOptions([kAXTrustedCheckOptionPrompt.takeUnretainedValue() as String: prompt] as CFDictionary)
    }
    static func value(_ element: AXUIElement, _ key: String) -> CFTypeRef? {
        var output: CFTypeRef?
        return AXUIElementCopyAttributeValue(element, key as CFString, &output) == .success ? output : nil
    }
    static func string(_ element: AXUIElement, _ key: String) -> String {
        if let text = value(element, key) as? String { return text }
        if let url = value(element, key) as? URL { return url.absoluteString }
        return ""
    }
    static func bool(_ element: AXUIElement, _ key: String) -> Bool { value(element, key) as? Bool ?? false }
    static func bounds(_ element: AXUIElement) -> Bounds? {
        guard let pos = value(element, kAXPositionAttribute), CFGetTypeID(pos) == AXValueGetTypeID(),
              let size = value(element, kAXSizeAttribute), CFGetTypeID(size) == AXValueGetTypeID() else { return nil }
        var point = CGPoint.zero, dimensions = CGSize.zero
        guard AXValueGetValue(unsafeBitCast(pos, to: AXValue.self), .cgPoint, &point),
              AXValueGetValue(unsafeBitCast(size, to: AXValue.self), .cgSize, &dimensions) else { return nil }
        let result = Bounds(x: point.x, y: point.y, width: dimensions.width, height: dimensions.height)
        return result.valid ? result : nil
    }
    static func set(_ element: AXUIElement, _ key: String, _ value: CFTypeRef) -> Bool {
        AXUIElementSetAttributeValue(element, key as CFString, value) == .success
    }
    static func setBounds(_ element: AXUIElement, _ bounds: Bounds) -> Bool {
        var point = CGPoint(x: bounds.x, y: bounds.y), size = CGSize(width: bounds.width, height: bounds.height)
        guard let p = AXValueCreate(.cgPoint, &point), let s = AXValueCreate(.cgSize, &size) else { return false }
        // Some apps constrain size using the current monitor, so position is applied before and after resize.
        let first = set(element, kAXPositionAttribute, p)
        let resized = set(element, kAXSizeAttribute, s)
        let moved = set(element, kAXPositionAttribute, p)
        return (first || moved) && resized
    }
    static func windows(app: AppInfo, displays: [Display], deadline: Date) -> [LiveWindow] {
        guard trusted(), let fallback = displays.first, Date() < deadline else { return [] }
        let application = AXUIElementCreateApplication(app.pid)
        AXUIElementSetMessagingTimeout(application, 0.4)
        guard let elements = value(application, kAXWindowsAttribute) as? [AXUIElement] else { return [] }
        var result: [LiveWindow] = []
        for element in elements.prefix(150) {
            if Date() >= deadline { break }
            AXUIElementSetMessagingTimeout(element, 0.25)
            guard string(element, kAXRoleAttribute) == kAXWindowRole else { continue }
            let subrole = string(element, kAXSubroleAttribute)
            if subrole == kAXDialogSubrole || subrole == kAXSystemDialogSubrole { continue }
            guard let frame = bounds(element) else { continue }
            let title = string(element, kAXTitleAttribute)
            let raw = string(element, kAXDocumentAttribute)
            let url = Validation.documentURL(raw)?.absoluteString ?? ""
            let display = displays.max { frame.overlap($0.area) < frame.overlap($1.area) } ?? fallback
            let record = WindowRecord(bundleID: app.bundleID, title: title, documentURL: url, entryIDs: [app.entry.id], bounds: frame, display: display,
                                      minimized: bool(element, kAXMinimizedAttribute), fullScreen: bool(element, "AXFullScreen"))
            result.append(LiveWindow(element: element, record: record, token: "\(app.pid):\(CFHash(element))"))
        }
        return result
    }
}

enum Desktop {
    @MainActor static func apps() -> [AppInfo] {
        NSWorkspace.shared.runningApplications.compactMap { app in
            guard app.activationPolicy == .regular, app.processIdentifier != ProcessInfo.processInfo.processIdentifier,
                  let id = app.bundleIdentifier, let url = app.bundleURL, !id.hasPrefix("local.openmemory.") else { return nil }
            return AppInfo(pid: app.processIdentifier, bundleID: id, path: url.path, name: app.localizedName ?? url.deletingPathExtension().lastPathComponent)
        }
    }
    @MainActor static func displays() -> [Display] {
        let top = NSScreen.screens.first?.frame.maxY ?? 0
        return NSScreen.screens.map { screen in
            let area = screen.visibleFrame
            return Display(id: String(describing: screen.deviceDescription[NSDeviceDescriptionKey("NSScreenNumber")] ?? "unknown"),
                           area: Bounds(x: area.minX, y: top - area.maxY, width: area.width, height: area.height))
        }
    }
    static func target(url: String, title: String, app: AppInfo) -> Entry? {
        guard let location = Validation.documentURL(url) else { return nil }
        // Do not probe NAS volumes during capture; the supplied URL is the evidence to preserve.
        let kind: EntryKind = location.isFileURL ? (location.hasDirectoryPath || app.bundleID == "com.apple.finder" ? .folder : .file) : .web
        let entry = Entry(kind: kind, bundleID: app.bundleID, appPath: app.path, appName: app.name,
                          title: title.isEmpty ? location.lastPathComponent : title, url: location.absoluteString,
                          note: kind == .web ? "감지된 URL만 열기 · 전체 탭 목록 아님" : "감지된 경로 열기 · 연결된 창 배치")
        return Validation.entry(entry) ? entry : nil
    }
    @MainActor static func capture() async -> Session {
        let apps = apps(), screens = displays()
        return await withCheckedContinuation { continuation in
            DispatchQueue.global(qos: .userInitiated).async {
                var session = Session()
                let deadline = Date().addingTimeInterval(20)
                if !Accessibility.trusted() { session.warnings.append("손쉬운 사용 권한이 없어 앱 목록만 기록했습니다. 권한 허용 후 다시 저장하세요.") }
                for app in apps {
                    session.entries.append(app.entry)
                    let live = Accessibility.windows(app: app, displays: screens, deadline: deadline)
                    for var window in live {
                        if let entry = target(url: window.record.documentURL, title: window.record.title, app: app) {
                            session.entries.append(entry); window.record.entryIDs = [entry.id]
                        }
                        session.windows.append(window.record)
                    }
                }
                if Date() >= deadline { session.warnings.append("일부 앱의 창 조회 시간이 초과되었습니다. 앱 목록은 보관했습니다.") }
                if let finder = apps.first(where: { $0.bundleID == "com.apple.finder" }) {
                    do {
                        let folders = try FinderFolders.read()
                        for folder in folders {
                            guard let entry = target(url: folder.url, title: folder.title, app: finder), entry.kind == .folder else { continue }
                            session.entries.append(entry)
                            let indexes = session.windows.indices.filter { session.windows[$0].bundleID == finder.bundleID && session.windows[$0].title == folder.title }
                            let sameTitles = folders.filter { $0.title == folder.title }.count
                            if indexes.count == 1 && sameTitles == 1 {
                                session.windows[indexes[0]].entryIDs = [entry.id]
                                session.windows[indexes[0]].documentURL = entry.url
                            }
                        }
                    } catch { session.warnings.append("Finder 폴더 연결: \(error.localizedDescription) · 자동화 권한을 확인하세요.") }
                }
                session.deduplicate()
                session.entries.sort { $0.kind != .app && $1.kind == .app }
                for index in session.entries.indices where session.entries[index].kind == .app {
                    let count = session.entries.filter { $0.bundleID == session.entries[index].bundleID && $0.kind != .app }.count
                    if count > 0 { session.entries[index].note = "연결된 문서·위치 \(count)개 (개별 행 참조)" }
                }
                continuation.resume(returning: session)
            }
        }
    }
    @MainActor static func liveWindows() async -> [LiveWindow] {
        let apps = apps(), screens = displays()
        return await withCheckedContinuation { continuation in
            DispatchQueue.global(qos: .userInitiated).async {
                let deadline = Date().addingTimeInterval(5)
                var windows = apps.flatMap { Accessibility.windows(app: $0, displays: screens, deadline: deadline) }
                if apps.contains(where: { $0.bundleID == "com.apple.finder" }), let folders = try? FinderFolders.read() {
                    for folder in folders where folders.filter({ $0.title == folder.title }).count == 1 {
                        let indexes = windows.indices.filter { windows[$0].record.bundleID == "com.apple.finder" && windows[$0].record.title == folder.title }
                        if indexes.count == 1 { windows[indexes[0]].record.documentURL = Validation.documentURL(folder.url)?.absoluteString ?? "" }
                    }
                }
                continuation.resume(returning: windows)
            }
        }
    }
}

struct FinderFolder: Decodable { var title: String; var url: String }
enum FinderFolders {
    static func read() throws -> [FinderFolder] {
        // Fixed read-only script. No paths, titles, or user data are interpolated into executable code.
        let source = """
        var app = Application('com.apple.finder');
        var result = [];
        if (app.running()) app.windows().forEach(function(w) {
            try { result.push({title: w.name(), url: w.target().url()}); } catch(e) {}
        });
        JSON.stringify(result);
        """
        let process = Process(); process.executableURL = URL(fileURLWithPath: "/usr/bin/osascript")
        process.arguments = ["-l", "JavaScript", "-e", source]
        let output = Pipe(), error = Pipe(); process.standardOutput = output; process.standardError = error
        // Drain both pipes while running so large Finder collections cannot deadlock the helper.
        let group = DispatchGroup(); let buffer = LockedData(); let errors = LockedData()
        try process.run()
        for (pipe, destination) in [(output, buffer), (error, errors)] {
            group.enter(); DispatchQueue.global().async { destination.set(pipe.fileHandleForReading.readDataToEndOfFile()); group.leave() }
        }
        let deadline = Date().addingTimeInterval(12)
        while process.isRunning && Date() < deadline { Thread.sleep(forTimeInterval: 0.05) }
        if process.isRunning { process.terminate(); throw NSError(domain: "OpenMemory", code: 1, userInfo: [NSLocalizedDescriptionKey: "폴더 조회 시간 초과"]) }
        group.wait()
        guard process.terminationStatus == 0 else { throw NSError(domain: "OpenMemory", code: 2, userInfo: [NSLocalizedDescriptionKey: "Finder 조회가 허용되지 않았습니다."]) }
        guard buffer.get().count <= 4 * 1024 * 1024 else { throw ModelError.tooLarge }
        return try JSONDecoder().decode([FinderFolder].self, from: buffer.get())
    }
}
private final class LockedData {
    private let lock = NSLock(); private var data = Data()
    func set(_ data: Data) { lock.lock(); self.data = data; lock.unlock() }
    func get() -> Data { lock.lock(); defer { lock.unlock() }; return data }
}
