import AppKit
import ApplicationServices
import OpenMemoryCore

@MainActor enum Restore {
    static func application(for entry: Entry) -> URL? {
        let recorded = URL(fileURLWithPath: entry.appPath)
        if Bundle(url: recorded)?.bundleIdentifier == entry.bundleID { return recorded }
        return NSWorkspace.shared.urlForApplication(withBundleIdentifier: entry.bundleID)
    }
    static func open(_ entry: Entry) async throws -> String {
        guard Validation.entry(entry), let application = application(for: entry) else { return "앱을 찾지 못했습니다. 기록은 유지합니다." }
        let options = NSWorkspace.OpenConfiguration(); options.activates = false; options.createsNewApplicationInstance = false
        if entry.kind == .app {
            if NSRunningApplication.runningApplications(withBundleIdentifier: entry.bundleID).isEmpty {
                try await withCheckedThrowingContinuation { (continuation: CheckedContinuation<Void, Error>) in
                    NSWorkspace.shared.openApplication(at: application, configuration: options) { _, error in
                        if let error { continuation.resume(throwing: error) } else { continuation.resume() }
                    }
                }
                return "앱 실행 요청 전달"
            }
            return "이미 실행 중"
        }
        guard let target = Validation.documentURL(entry.url) else { return "지원하지 않는 문서 경로" }
        let exists = target.isFileURL ? await FileProbe.exists(target.path) : true
        if exists != true {
            var fallback = entry; fallback.kind = .app; fallback.url = ""
            _ = try await open(fallback)
            return exists == nil ? "공유폴더 응답 시간 초과 · 기록을 유지하고 앱만 실행" : "파일 없음 · 기록을 유지하고 앱만 실행"
        }
        try await withCheckedThrowingContinuation { (continuation: CheckedContinuation<Void, Error>) in
            NSWorkspace.shared.open([target], withApplicationAt: application, configuration: options) { _, error in
                if let error { continuation.resume(throwing: error) } else { continuation.resume() }
            }
        }
        return "열기 요청 전달 · 앱의 문서 열림 확인은 별도"
    }
    static func run(_ session: Session, log: (String) -> Void) async throws {
        guard Validation.session(session) else { throw ModelError.invalidRecord }
        let active = await Desktop.capture()
        let alreadyOpen = Set(active.entries.map(\.id))
        for entry in session.entries {
            try Task.checkCancellation()
            if alreadyOpen.contains(entry.id) { log("\(entry.title) — 이미 열려 있음") }
            else {
                do { log("\(entry.title) — \(try await open(entry))") }
                catch { log("\(entry.title) — \(error.localizedDescription)") }
                try await Task.sleep(nanoseconds: 400_000_000)
            }
        }
        guard !session.windows.isEmpty else { log("창 배치 기록이 없습니다. 현재 작업을 새로 저장하세요."); return }
        guard Accessibility.trusted() else { log("손쉬운 사용 권한이 없어 창 위치를 적용하지 못했습니다."); return }
        var pending = session.windows, used = Set<String>(), observed: [String: String] = [:]
        var verified = 0
        let deadline = Date().addingTimeInterval(45)
        while !pending.isEmpty && Date() < deadline {
            try Task.checkCancellation()
            let available = await Desktop.liveWindows().filter { !used.contains($0.token) }
            for window in pending {
                guard let index = Matcher.index(saved: window, candidates: available.map(\.record), savedGroup: session.windows) else { continue }
                let live = available[index]
                if used.contains(live.token) { continue }
                guard observed[window.id] == live.token else { observed[window.id] = live.token; continue }
                let result = try await place(window, live: live, displays: Desktop.displays())
                if result { verified += 1 }
                log("\(window.title) — " + (result ? "창 위치·크기·상태 확인" : "앱이 창 배치 적용을 거부했거나 확인되지 않음"))
                pending.removeAll { $0.id == window.id }; used.insert(live.token)
            }
            if !pending.isEmpty { try await Task.sleep(nanoseconds: 1_000_000_000) }
        }
        for window in pending { log("\(window.title) — 창을 찾지 못했거나 구분 불가") }
        log("창 배치 복원 확인 \(verified)/\(session.windows.count)개")
    }
    static func place(_ saved: WindowRecord, live: LiveWindow, displays: [Display]) async throws -> Bool {
        let element = live.element
        if Accessibility.bool(element, "AXFullScreen") {
            guard Accessibility.set(element, "AXFullScreen", kCFBooleanFalse) else { return false }
            try await Task.sleep(nanoseconds: 800_000_000)
        }
        if Accessibility.bool(element, kAXMinimizedAttribute) {
            guard Accessibility.set(element, kAXMinimizedAttribute, kCFBooleanFalse) else { return false }
            try await Task.sleep(nanoseconds: 300_000_000)
        }
        let destination = saved.destination(displays: displays)
        let moved = Accessibility.setBounds(element, destination)
        try await Task.sleep(nanoseconds: 300_000_000)
        let geometry = Accessibility.bounds(element)?.approximatelyEquals(destination) ?? false
        if saved.fullScreen {
            guard Accessibility.set(element, "AXFullScreen", kCFBooleanTrue) else { return false }
            try await Task.sleep(nanoseconds: 800_000_000)
        }
        if saved.minimized {
            guard Accessibility.set(element, kAXMinimizedAttribute, kCFBooleanTrue) else { return false }
            try await Task.sleep(nanoseconds: 300_000_000)
        }
        return moved && geometry && Accessibility.bool(element, kAXMinimizedAttribute) == saved.minimized
            && Accessibility.bool(element, "AXFullScreen") == saved.fullScreen
    }
}

private enum FileProbe {
    static func exists(_ path: String) async -> Bool? {
        await withCheckedContinuation { continuation in
            let completion = ProbeCompletion(continuation)
            DispatchQueue.global(qos: .utility).async { completion.finish(FileManager.default.fileExists(atPath: path)) }
            DispatchQueue.global(qos: .utility).asyncAfter(deadline: .now() + 8) { completion.finish(nil) }
        }
    }
}
private final class ProbeCompletion: @unchecked Sendable {
    private let lock = NSLock()
    private var continuation: CheckedContinuation<Bool?, Never>?
    init(_ continuation: CheckedContinuation<Bool?, Never>) { self.continuation = continuation }
    func finish(_ value: Bool?) {
        lock.lock(); let callback = continuation; continuation = nil; lock.unlock()
        callback?.resume(returning: value)
    }
}
