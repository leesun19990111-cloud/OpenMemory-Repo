import Foundation

public enum EntryKind: String, Codable, Sendable { case app, file, folder, web }

public struct Entry: Codable, Equatable, Identifiable, Sendable {
    public var id: String
    public var kind: EntryKind
    public var bundleID: String
    public var appPath: String
    public var appName: String
    public var title: String
    public var url: String
    public var note: String
    public init(kind: EntryKind, bundleID: String, appPath: String, appName: String, title: String, url: String = "", note: String = "") {
        self.kind = kind; self.bundleID = bundleID; self.appPath = appPath
        self.appName = appName; self.title = title; self.url = url; self.note = note
        self.id = kind.rawValue + "|" + bundleID + "|" + (kind == .app ? appPath : url)
    }
}

public struct Bounds: Codable, Equatable, Sendable {
    public var x: Double, y: Double, width: Double, height: Double
    public init(x: Double, y: Double, width: Double, height: Double) {
        self.x = x; self.y = y; self.width = width; self.height = height
    }
    public var valid: Bool {
        [x, y, width, height].allSatisfy { $0.isFinite && abs($0) < 1_000_000 }
        && width > 0 && height > 0
    }
    public func fitted(to area: Bounds) -> Bounds {
        let w = min(width, area.width), h = min(height, area.height)
        return Bounds(x: max(area.x, min(x, area.x + area.width - w)),
                      y: max(area.y, min(y, area.y + area.height - h)), width: w, height: h)
    }
    public func approximatelyEquals(_ other: Bounds, tolerance: Double = 3) -> Bool {
        abs(x - other.x) <= tolerance && abs(y - other.y) <= tolerance
        && abs(width - other.width) <= tolerance && abs(height - other.height) <= tolerance
    }
    public func overlap(_ other: Bounds) -> Double {
        max(0, min(x + width, other.x + other.width) - max(x, other.x))
        * max(0, min(y + height, other.y + other.height) - max(y, other.y))
    }
}

public struct Display: Codable, Equatable, Sendable {
    public var id: String
    public var area: Bounds
    public init(id: String, area: Bounds) { self.id = id; self.area = area }
}

public struct WindowRecord: Codable, Equatable, Identifiable, Sendable {
    public var id: String
    public var bundleID: String
    public var title: String
    public var documentURL: String
    public var entryIDs: [String]
    public var bounds: Bounds
    public var display: Display
    public var minimized: Bool
    public var fullScreen: Bool
    public init(id: String = UUID().uuidString, bundleID: String, title: String, documentURL: String = "", entryIDs: [String], bounds: Bounds, display: Display, minimized: Bool = false, fullScreen: Bool = false) {
        self.id = id; self.bundleID = bundleID; self.title = title; self.documentURL = documentURL
        self.entryIDs = entryIDs; self.bounds = bounds; self.display = display
        self.minimized = minimized; self.fullScreen = fullScreen
    }
    public func destination(displays: [Display]) -> Bounds {
        guard !displays.isEmpty else { return bounds }
        if let same = displays.first(where: { $0.id == display.id }) {
            if same.area == display.area && bounds.overlap(same.area) > 0 { return bounds }
            return bounds.fitted(to: same.area)
        }
        let nearest = displays.max { bounds.overlap($0.area) < bounds.overlap($1.area) }!
        return bounds.fitted(to: nearest.area)
    }
}

public struct Session: Codable, Equatable, Sendable {
    public var schema = 1
    public var platform = "macOS"
    public var created = Date()
    public var entries: [Entry] = []
    public var windows: [WindowRecord] = []
    public var warnings: [String] = []
    public init() {}
    public mutating func remove(ids: Set<String>) {
        entries.removeAll { ids.contains($0.id) }
        let remaining = Set(entries.map(\.id))
        windows = windows.compactMap { window in
            var kept = window; kept.entryIDs.removeAll { !remaining.contains($0) }
            return kept.entryIDs.isEmpty ? nil : kept
        }
    }
    public mutating func deduplicate() {
        var seen = Set<String>(); entries.removeAll { !seen.insert($0.id).inserted }
    }
}

public enum ModelError: Error, LocalizedError {
    case invalidRecord, emptyCapture, tooLarge
    public var errorDescription: String? {
        switch self {
        case .invalidRecord: return "지원하지 않거나 손상된 Mac 작업 기록입니다."
        case .emptyCapture: return "감지된 작업이 없어 기존 기록을 유지했습니다."
        case .tooLarge: return "작업 기록의 허용 크기를 초과했습니다."
        }
    }
}

public enum Validation {
    public static func documentURL(_ raw: String) -> URL? {
        guard !raw.isEmpty, !raw.contains("\0"), !raw.contains("\n"), !raw.contains("\r") else { return nil }
        let candidate: URL?
        if raw.hasPrefix("/") { candidate = URL(fileURLWithPath: raw) }
        else { candidate = URL(string: raw) }
        guard let url = candidate else { return nil }
        if url.isFileURL {
            guard (url.host ?? "").isEmpty || url.host == "localhost" else { return nil }
            guard url.path.hasPrefix("/") else { return nil }
            let forbidden = ["app", "sh", "command", "scpt", "scptd", "workflow", "pkg", "dmg", "exe", "bat", "ps1"]
            guard !forbidden.contains(url.pathExtension.lowercased()) else { return nil }
            return URL(fileURLWithPath: url.path).standardizedFileURL
        }
        guard ["https", "http"].contains(url.scheme?.lowercased() ?? ""),
              !(url.host ?? "").isEmpty, url.user == nil, url.password == nil else { return nil }
        return url
    }
    public static func entry(_ entry: Entry) -> Bool {
        guard !entry.bundleID.isEmpty, entry.bundleID.count < 256,
              entry.appPath.hasPrefix("/"), entry.appPath.hasSuffix(".app"),
              !entry.appPath.contains("\0"), entry.title.count < 32768 else { return false }
        if entry.kind == .app { return entry.url.isEmpty }
        guard let url = documentURL(entry.url) else { return false }
        return entry.kind == .web ? !url.isFileURL : url.isFileURL
    }
    public static func session(_ session: Session) -> Bool {
        guard session.schema == 1, session.platform == "macOS", session.entries.count <= 10000,
              session.windows.count <= 10000, session.entries.allSatisfy(entry),
              Set(session.entries.map(\.id)).count == session.entries.count else { return false }
        let keys = Set(session.entries.map(\.id))
        return session.windows.allSatisfy { window in
            window.bounds.valid && window.display.area.valid && !window.entryIDs.isEmpty
            && window.entryIDs.allSatisfy(keys.contains)
        }
    }
}

public enum Matcher {
    public static func title(_ text: String) -> String {
        text.replacingOccurrences(of: "*", with: "").replacingOccurrences(of: "●", with: "")
            .trimmingCharacters(in: .whitespacesAndNewlines).lowercased()
    }
    public static func index(saved: WindowRecord, candidates: [WindowRecord], savedGroup: [WindowRecord]) -> Int? {
        let sameApp = candidates.indices.filter { candidates[$0].bundleID == saved.bundleID }
        if !saved.documentURL.isEmpty {
            let exact = sameApp.filter { candidates[$0].documentURL == saved.documentURL }
            if exact.count == 1 && savedGroup.filter({ $0.bundleID == saved.bundleID && $0.documentURL == saved.documentURL }).count == 1 { return exact[0] }
            // Never match the title of a known different document in the same app.
            let byTitle = sameApp.filter { candidates[$0].documentURL.isEmpty && title(candidates[$0].title) == title(saved.title) }
            if byTitle.count == 1 && !title(saved.title).isEmpty && savedGroup.filter({ $0.bundleID == saved.bundleID && title($0.title) == title(saved.title) }).count == 1 { return byTitle[0] }
            return nil
        }
        let exact = sameApp.filter { title(candidates[$0].title) == title(saved.title) }
        if exact.count == 1 && !title(saved.title).isEmpty && savedGroup.filter({ $0.bundleID == saved.bundleID && title($0.title) == title(saved.title) }).count == 1 { return exact[0] }
        if sameApp.count == 1 && savedGroup.filter({ $0.bundleID == saved.bundleID }).count == 1 { return sameApp[0] }
        return nil
    }
}
