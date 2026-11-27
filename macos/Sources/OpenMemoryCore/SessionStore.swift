import Foundation

public final class SessionStore {
    public let root: URL
    public var latest: URL { root.appendingPathComponent("session.json") }
    public init(root: URL? = nil) {
        self.root = root ?? FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask)[0].appendingPathComponent("OpenMemoryMac", isDirectory: true)
    }
    public func load() throws -> Session? {
        guard FileManager.default.fileExists(atPath: latest.path) else { return nil }
        let size = (try FileManager.default.attributesOfItem(atPath: latest.path)[.size] as? NSNumber)?.intValue ?? 0
        guard size <= 8 * 1024 * 1024 else { throw ModelError.tooLarge }
        let session = try JSONDecoder().decode(Session.self, from: Data(contentsOf: latest))
        guard Validation.session(session) else { throw ModelError.invalidRecord }
        return session
    }
    public func save(_ session: Session, allowEmpty: Bool = false) throws {
        guard allowEmpty || !session.entries.isEmpty else { throw ModelError.emptyCapture }
        guard Validation.session(session) else { throw ModelError.invalidRecord }
        try FileManager.default.createDirectory(at: root, withIntermediateDirectories: true, attributes: [.posixPermissions: 0o700])
        let encoder = JSONEncoder(); encoder.outputFormatting = [.prettyPrinted, .sortedKeys]
        let data = try encoder.encode(session)
        guard data.count <= 8 * 1024 * 1024 else { throw ModelError.tooLarge }
        try data.write(to: latest, options: .atomic)
        try FileManager.default.setAttributes([.posixPermissions: 0o600], ofItemAtPath: latest.path)
    }
    public func reset() throws {
        guard FileManager.default.fileExists(atPath: latest.path) else { return }
        let backup = root.appendingPathComponent("reset-backups", isDirectory: true)
        try FileManager.default.createDirectory(at: backup, withIntermediateDirectories: true, attributes: [.posixPermissions: 0o700])
        try FileManager.default.moveItem(at: latest, to: backup.appendingPathComponent(UUID().uuidString + ".json"))
    }
}
