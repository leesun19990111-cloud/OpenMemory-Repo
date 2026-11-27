import XCTest
@testable import OpenMemoryCore

final class CoreTests: XCTestCase {
    private func entry(_ path: String = "/Users/test/설계 & 자료.xlsx") -> Entry {
        Entry(kind: .file, bundleID: "com.microsoft.Excel", appPath: "/Applications/Microsoft Excel.app", appName: "Excel", title: "설계 & 자료", url: URL(fileURLWithPath: path).absoluteString)
    }
    private func window(_ title: String = "설계", url: String = "") -> WindowRecord {
        WindowRecord(bundleID: "com.microsoft.Excel", title: title, documentURL: url, entryIDs: [entry().id], bounds: Bounds(x: 10, y: 30, width: 800, height: 600), display: Display(id: "1", area: Bounds(x: 0, y: 25, width: 1920, height: 1055)))
    }
    private func temporaryStore() throws -> SessionStore {
        let root = FileManager.default.temporaryDirectory.appendingPathComponent("OpenMemoryTests-" + UUID().uuidString)
        addTeardownBlock { try? FileManager.default.removeItem(at: root) }
        return SessionStore(root: root)
    }
    func testUnicodeFileURL() { XCTAssertEqual(Validation.documentURL(entry().url)?.path, "/Users/test/설계 & 자료.xlsx") }
    func testLocalhostCanonicalization() { XCTAssertEqual(Validation.documentURL("file://localhost/Users/test/a.xlsx")?.absoluteString, "file:///Users/test/a.xlsx") }
    func testAbsolutePaths() { XCTAssertEqual(Validation.documentURL("/Volumes/Shared/A B.pdf")?.path, "/Volumes/Shared/A B.pdf") }
    func testRelativePathRejected() { XCTAssertNil(Validation.documentURL("relative.docx")) }
    func testExecutableTargetsRejected() { for path in ["/tmp/test.sh", "/tmp/app.app", "/tmp/test.command", "/tmp/a.pkg"] { XCTAssertNil(Validation.documentURL(path)) } }
    func testUnsafeSchemesRejected() { for value in ["javascript:alert(1)", "data:text/html,x", "smb://server/share", "https://name:secret@example.com"] { XCTAssertNil(Validation.documentURL(value)) } }
    func testWebURLAllowed() { XCTAssertEqual(Validation.documentURL("https://www.figma.com/design/example")?.host, "www.figma.com") }
    func testNonLocalFileHostRejected() { XCTAssertNil(Validation.documentURL("file://remote/path.docx")) }
    func testInvalidGeometryRejected() { XCTAssertFalse(Bounds(x: .infinity, y: 0, width: 10, height: 10).valid); XCTAssertFalse(Bounds(x: 0, y: 0, width: -1, height: 4).valid) }
    func testNegativeMonitorGeometry() { let area = Bounds(x: -1920, y: 0, width: 1920, height: 1040); XCTAssertEqual(Bounds(x: 8000, y: -20, width: 2200, height: 1200).fitted(to: area), area) }
    func testUnchangedScreenPreservesSpanningWindow() { var w = window(); w.bounds.x = -50; XCTAssertEqual(w.destination(displays: [w.display]), w.bounds) }
    func testMissingDisplayFitsWindow() { let w = window(); let display = Display(id: "new", area: Bounds(x: -800, y: 0, width: 800, height: 600)); XCTAssertEqual(w.destination(displays: [display]), display.area) }
    func testUniqueDocumentMatchingAfterTitleChanges() { let w = window(url: entry().url); var current = w; current.title = "read only"; XCTAssertEqual(Matcher.index(saved: w, candidates: [current], savedGroup: [w]), 0) }
    func testDifferentDocumentIsNotMatchedBySameTitle() { let w = window(url: entry().url); var other = w; other.documentURL = entry("/tmp/other.xlsx").url; XCTAssertNil(Matcher.index(saved: w, candidates: [other], savedGroup: [w])) }
    func testAmbiguousTitlesAreNotMatched() { let w = window(); XCTAssertNil(Matcher.index(saved: w, candidates: [w, w], savedGroup: [w, w])) }
    func testTitleNormalization() { XCTAssertEqual(Matcher.title("  설계 * ● "), Matcher.title("설계")) }
    func testCrossAppNotMatched() { let w = window(); var other = w; other.bundleID = "other.app"; XCTAssertNil(Matcher.index(saved: w, candidates: [other], savedGroup: [w])) }
    func testRemovingLastEntryRemovesGeometry() { var s = Session(); s.entries = [entry()]; s.windows = [window()]; s.remove(ids: [entry().id]); XCTAssertTrue(s.entries.isEmpty); XCTAssertTrue(s.windows.isEmpty) }
    func testRemoveKeepsUnrelatedEntry() { var s = Session(); let other = entry("/tmp/other.xlsx"); s.entries = [entry(), other]; s.windows = [window()]; s.remove(ids: [entry().id]); XCTAssertEqual(s.entries, [other]); XCTAssertTrue(s.windows.isEmpty) }
    func testFreshCaptureHasNoExclusionBlacklist() { var s = Session(); s.entries = [entry()]; s.remove(ids: [entry().id]); var fresh = Session(); fresh.entries = [entry()]; XCTAssertEqual(fresh.entries.count, 1) }
    func testDeduplicationKeepsCaseSensitivePaths() { var s = Session(); s.entries = [entry("/tmp/a.xlsx"), entry("/tmp/a.xlsx"), entry("/tmp/A.xlsx")]; s.deduplicate(); XCTAssertEqual(s.entries.count, 2) }
    func testAtomicRoundTrip() throws { let store = try temporaryStore(); var s = Session(); s.entries = [entry()]; s.windows = [window()]; try store.save(s); XCTAssertEqual(try store.load(), s); s.warnings = ["메시지 <&>"]; try store.save(s); XCTAssertEqual(try store.load(), s) }
    func testEmptyCapturePreservesSavedRecord() throws { let store = try temporaryStore(); var s = Session(); s.entries = [entry()]; try store.save(s); XCTAssertThrowsError(try store.save(Session())); XCTAssertEqual(try store.load(), s) }
    func testEditedEmptyRecordAllowed() throws { let store = try temporaryStore(); try store.save(Session(), allowEmpty: true); XCTAssertEqual(try store.load()?.entries.count, 0) }
    func testResetPreservesUnrelatedFilesAndBackup() throws { let store = try temporaryStore(); var s = Session(); s.entries = [entry()]; try store.save(s); let file = store.root.appendingPathComponent("keep.txt"); try Data("keep".utf8).write(to: file); try store.reset(); XCTAssertNil(try store.load()); XCTAssertTrue(FileManager.default.fileExists(atPath: file.path)); XCTAssertEqual(try FileManager.default.contentsOfDirectory(atPath: store.root.appendingPathComponent("reset-backups").path).count, 1) }
    func testForeignSchemaRejected() throws { let store = try temporaryStore(); var s = Session(); s.schema = 999; XCTAssertThrowsError(try store.save(s, allowEmpty: true)) }
    func testDanglingWindowRejected() { var s = Session(); s.windows = [window()]; XCTAssertFalse(Validation.session(s)) }
    func testTruncatedJSONRejected() throws { let store = try temporaryStore(); try FileManager.default.createDirectory(at: store.root, withIntermediateDirectories: true); try Data("{broken".utf8).write(to: store.latest); XCTAssertThrowsError(try store.load()) }
}
