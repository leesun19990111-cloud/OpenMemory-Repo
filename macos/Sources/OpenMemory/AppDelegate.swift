import AppKit
import Carbon
import ServiceManagement
import OpenMemoryCore

@MainActor final class AppDelegate: NSObject, NSApplicationDelegate, NSTableViewDataSource, NSTableViewDelegate, NSWindowDelegate, NSMenuDelegate {
    private let store = SessionStore()
    private var session = Session()
    private var window: NSWindow!
    private let table = NSTableView()
    private let details = NSTextView()
    private let status = NSTextField(labelWithString: "준비됨 · 이 Mac에 작업 기록 저장")
    private var item: NSStatusItem!
    private var menu = NSMenu()
    private var startup: NSButton!
    private var actions: [NSButton] = []
    private var menuActions: [NSMenuItem] = []
    private var task: Task<Void, Never>?
    private var busy = false

    func renderPreview(to url: URL) throws {
        setupWindow()
        session.entries = [
            Entry(kind: .file, bundleID: "com.microsoft.Powerpoint", appPath: "/Applications/Microsoft PowerPoint.app", appName: "PowerPoint", title: "프로젝트 제안서.pptx", url: "file:///tmp/example.pptx", note: "파일·창 배치"),
            Entry(kind: .folder, bundleID: "com.apple.finder", appPath: "/System/Library/CoreServices/Finder.app", appName: "Finder", title: "설계 작업", url: "file:///tmp/example/", note: "폴더·창 배치"),
            Entry(kind: .file, bundleID: "com.adobe.Photoshop", appPath: "/Applications/Adobe Photoshop.app", appName: "Photoshop", title: "레이아웃.psd", url: "file:///tmp/example.psd", note: "감지된 파일 경로"),
            Entry(kind: .app, bundleID: "com.figma.Desktop", appPath: "/Applications/Figma.app", appName: "Figma", title: "Figma", note: "앱 실행·제목 보관"),
            Entry(kind: .app, bundleID: "com.apple.Safari", appPath: "/Applications/Safari.app", appName: "Safari", title: "Safari", note: "앱 실행·창 배치")
        ]
        table.reloadData(); table.selectRowIndexes(IndexSet(integer: 1), byExtendingSelection: false)
        details.string = "창을 이동하려면 손쉬운 사용 권한이 필요합니다.\n원본 문서의 수정 내용은 각 앱에서 먼저 저장하세요."
        status.stringValue = "실제 Mac 앱 화면 · 예시 작업 목록 (개인 작업 기록 아님)"
        window.makeKeyAndOrderFront(nil)
        RunLoop.current.run(until: Date().addingTimeInterval(0.5))
        let view = window.contentView!; view.layoutSubtreeIfNeeded(); view.displayIfNeeded()
        guard let bitmap = view.bitmapImageRepForCachingDisplay(in: view.bounds) else { throw ModelError.invalidRecord }
        view.cacheDisplay(in: view.bounds, to: bitmap)
        guard let png = bitmap.representation(using: .png, properties: [:]) else { throw ModelError.invalidRecord }
        try png.write(to: url)
        window.orderOut(nil)
    }

    func applicationDidFinishLaunching(_ notification: Notification) {
        setupWindow(); setupMenu()
        do { session = try store.load() ?? Session() } catch { append(error.localizedDescription) }
        table.reloadData()
        let event = NSAppleEventManager.shared().currentAppleEvent
        let login = event?.eventID == kAEOpenApplication && event?.paramDescriptor(forKeyword: keyAEPropData)?.enumCodeValue == keyAELaunchedAsLogInItem
        if login && SMAppService.mainApp.status == .enabled {
            task = Task { try? await Task.sleep(nanoseconds: 15_000_000_000); if !Task.isCancelled { restore() } }
        } else { showWindow() }
    }
    func applicationShouldHandleReopen(_ sender: NSApplication, hasVisibleWindows flag: Bool) -> Bool { showWindow(); return true }
    func applicationShouldTerminateAfterLastWindowClosed(_ sender: NSApplication) -> Bool { false }
    func windowShouldClose(_ sender: NSWindow) -> Bool { sender.orderOut(nil); return false }

    private func setupWindow() {
        window = NSWindow(contentRect: NSRect(x: 0, y: 0, width: 1050, height: 690), styleMask: [.titled, .closable, .miniaturizable, .resizable], backing: .buffered, defer: false)
        window.title = "OpenMemory · 작업 이어 열기"; window.minSize = NSSize(width: 850, height: 560)
        window.backgroundColor = NSColor(calibratedWhite: 0.97, alpha: 1)
        window.delegate = self; window.isReleasedWhenClosed = false; window.center()
        let content = window.contentView!
        let stack = NSStackView(); stack.orientation = .vertical; stack.alignment = .leading; stack.spacing = 16
        stack.translatesAutoresizingMaskIntoConstraints = false; content.addSubview(stack)
        NSLayoutConstraint.activate([stack.leadingAnchor.constraint(equalTo: content.leadingAnchor, constant: 24), stack.trailingAnchor.constraint(equalTo: content.trailingAnchor, constant: -24), stack.topAnchor.constraint(equalTo: content.topAnchor, constant: 22), stack.bottomAnchor.constraint(equalTo: content.bottomAnchor, constant: -18)])
        let brand = NSStackView(); brand.orientation = .horizontal; brand.spacing = 18
        let logo = NSImageView()
        if let url = Bundle.main.url(forResource: "OpenMemory", withExtension: "png") { logo.image = NSImage(contentsOf: url) }
        logo.imageScaling = .scaleProportionallyUpOrDown
        logo.widthAnchor.constraint(equalToConstant: 88).isActive = true; logo.heightAnchor.constraint(equalToConstant: 88).isActive = true
        let brandText = NSStackView(); brandText.orientation = .vertical; brandText.alignment = .leading; brandText.spacing = 6
        let heading = NSTextField(labelWithString: "OpenMemory"); heading.font = .systemFont(ofSize: 34, weight: .bold)
        let subtitle = NSTextField(labelWithString: "앱·문서·폴더와 창 배치를 기억합니다"); subtitle.textColor = .secondaryLabelColor
        brandText.addArrangedSubview(heading); brandText.addArrangedSubview(subtitle)
        brand.addArrangedSubview(logo); brand.addArrangedSubview(brandText); stack.addArrangedSubview(brand)
        let toolbar = NSStackView(); toolbar.orientation = .horizontal; toolbar.spacing = 10
        for (title, action) in [("현재 작업 저장", #selector(save)), ("저장된 파일 열기", #selector(restore)), ("목록에서 제외", #selector(remove)), ("권한 설정", #selector(permissions))] {
            let button = NSButton(title: title, target: self, action: action); button.bezelStyle = .rounded
            button.controlSize = .large; button.font = .systemFont(ofSize: 14, weight: .medium)
            button.heightAnchor.constraint(equalToConstant: 44).isActive = true
            button.widthAnchor.constraint(greaterThanOrEqualToConstant: 155).isActive = true
            if title == "현재 작업 저장" { button.bezelColor = .systemBlue; button.contentTintColor = .white }
            toolbar.addArrangedSubview(button); actions.append(button)
        }
        toolbar.addArrangedSubview(NSButton(title: "복원 중지", target: self, action: #selector(cancel)))
        stack.addArrangedSubview(toolbar)
        let scroll = NSScrollView(); scroll.hasVerticalScroller = true; scroll.borderType = .noBorder
        scroll.wantsLayer = true; scroll.layer?.cornerRadius = 10; scroll.layer?.borderWidth = 1
        scroll.layer?.borderColor = NSColor.separatorColor.cgColor
        table.usesAlternatingRowBackgroundColors = true; table.allowsMultipleSelection = true; table.rowHeight = 30
        table.delegate = self; table.dataSource = self
        for (id, name, width) in [("order", "순서", 45.0), ("title", "대상", 250.0), ("kind", "종류", 75.0), ("app", "앱", 150.0), ("note", "복원 범위", 420.0)] {
            let column = NSTableColumn(identifier: NSUserInterfaceItemIdentifier(id)); column.title = name; column.width = width; table.addTableColumn(column)
        }
        scroll.documentView = table; stack.addArrangedSubview(scroll)
        scroll.widthAnchor.constraint(equalTo: stack.widthAnchor).isActive = true
        scroll.heightAnchor.constraint(greaterThanOrEqualToConstant: 210).isActive = true
        let messageScroll = NSScrollView(); messageScroll.hasVerticalScroller = true; messageScroll.borderType = .noBorder
        messageScroll.wantsLayer = true; messageScroll.layer?.cornerRadius = 10
        details.isEditable = false; details.isRichText = false; details.font = .systemFont(ofSize: 12)
        details.backgroundColor = NSColor(calibratedWhite: 0.935, alpha: 1); details.textContainerInset = NSSize(width: 12, height: 12)
        details.autoresizingMask = [.width]; details.textContainer?.widthTracksTextView = true
        details.string = "창을 이동하려면 손쉬운 사용 권한이 필요합니다.\n원본 문서의 수정 내용은 각 앱에서 먼저 저장하세요.\n연결이 없는 문서는 제목을 보관하고 앱만 실행합니다. 전체 내부 탭 구성은 복원하지 않습니다."
        messageScroll.documentView = details; stack.addArrangedSubview(messageScroll)
        messageScroll.widthAnchor.constraint(equalTo: stack.widthAnchor).isActive = true
        messageScroll.heightAnchor.constraint(equalToConstant: 110).isActive = true
        status.font = .systemFont(ofSize: 12); status.textColor = .secondaryLabelColor; stack.addArrangedSubview(status)
    }
    private func setupMenu() {
        item = NSStatusBar.system.statusItem(withLength: NSStatusItem.squareLength)
        if let url = Bundle.main.url(forResource: "OpenMemory", withExtension: "png"), let icon = NSImage(contentsOf: url) { icon.size = NSSize(width: 22, height: 22); item.button?.image = icon }
        item.button?.toolTip = "OpenMemory"
        menu.delegate = self; menu.autoenablesItems = false
        for (title, action) in [("현재 작업 저장", #selector(save)), ("저장된 작업 초기화", #selector(reset)), ("저장된 파일 열기", #selector(restore))] {
            let entry = NSMenuItem(title: title, action: action, keyEquivalent: ""); entry.target = self
            menu.addItem(entry); menuActions.append(entry)
        }
        menu.addItem(.separator())
        let loginItem = NSMenuItem()
        let wrapper = NSView(frame: NSRect(x: 0, y: 0, width: 260, height: 32))
        startup = NSButton(checkboxWithTitle: "로그인 시 자동 복원", target: self, action: #selector(toggleStartup))
        startup.frame = NSRect(x: 16, y: 5, width: 230, height: 22); wrapper.addSubview(startup); loginItem.view = wrapper
        menu.addItem(loginItem); menu.addItem(.separator())
        for (title, action) in [("목록 창 열기", #selector(showWindow)), ("OpenMemory 종료", #selector(quit))] {
            let entry = NSMenuItem(title: title, action: action, keyEquivalent: ""); entry.target = self; menu.addItem(entry)
        }
        item.menu = menu
    }
    func menuWillOpen(_ menu: NSMenu) { refreshStartup() }
    private func refreshStartup() {
        startup.state = SMAppService.mainApp.status == .enabled ? .on : .off
        startup.title = SMAppService.mainApp.status == .requiresApproval ? "로그인 설정 승인 필요" : "로그인 시 자동 복원"
    }
    @objc private func toggleStartup() {
        do {
            if SMAppService.mainApp.status == .enabled || SMAppService.mainApp.status == .requiresApproval { try SMAppService.mainApp.unregister() }
            else { try SMAppService.mainApp.register() }
            if SMAppService.mainApp.status == .requiresApproval { SMAppService.openSystemSettingsLoginItems() }
        } catch { showError(error) }
        refreshStartup()
    }
    @objc private func permissions() {
        _ = Accessibility.trusted(prompt: true)
        NSWorkspace.shared.open(URL(string: "x-apple.systempreferences:com.apple.preference.security?Privacy_Accessibility")!)
        status.stringValue = "손쉬운 사용에서 OpenMemory를 허용한 뒤 현재 작업을 다시 저장하세요."
    }
    @objc private func showWindow() { window.makeKeyAndOrderFront(nil); NSApp.activate(ignoringOtherApps: true) }
    @objc private func save() {
        guard !busy else { return }; task?.cancel(); setBusy(true); status.stringValue = "열린 작업과 창 배치를 확인 중…"
        task = Task {
            let snapshot = await Desktop.capture()
            do {
                try Task.checkCancellation(); try store.save(snapshot); session = snapshot; table.reloadData()
                details.string = snapshot.warnings.joined(separator: "\n")
                status.stringValue = "저장 완료 · 작업 \(snapshot.entries.count)개 · 창 배치 \(snapshot.windows.count)개"
            } catch { showError(error) }
            setBusy(false)
        }
    }
    @objc private func restore() {
        guard !busy else { return }; task?.cancel()
        do { session = try store.load() ?? Session(); table.reloadData() } catch { showError(error); return }
        guard !session.entries.isEmpty else { status.stringValue = "저장된 작업이 없습니다."; return }
        setBusy(true); details.string = ""; status.stringValue = "작업 열기 및 창 배치 복원 중…"
        let snapshot = session
        task = Task {
            do { try await Restore.run(snapshot, log: append); status.stringValue = "복원 처리 종료 · 아래 결과를 확인하세요." }
            catch is CancellationError { status.stringValue = "복원을 중지했습니다. 이미 전달한 열기 요청은 계속될 수 있습니다." }
            catch { showError(error) }
            setBusy(false)
        }
    }
    @objc private func remove() {
        guard !busy else { return }; task?.cancel()
        let ids = Set(table.selectedRowIndexes.filter { $0 < session.entries.count }.map { session.entries[$0].id })
        guard !ids.isEmpty else { status.stringValue = "제외할 행을 먼저 선택하세요."; return }
        var edited = session; edited.remove(ids: ids)
        do { try store.save(edited, allowEmpty: true); session = edited; table.reloadData(); status.stringValue = "선택한 \(ids.count)개를 기록에서 제외했습니다." } catch { showError(error) }
    }
    @objc private func reset() {
        guard !busy else { return }; task?.cancel()
        do { try store.reset(); session = Session(); table.reloadData(); details.string = "이전 기록은 reset-backups에 보관했습니다. 원본 업무 파일은 변경하지 않았습니다."; status.stringValue = "저장된 작업 초기화 완료" } catch { showError(error) }
    }
    @objc private func cancel() { task?.cancel() }
    @objc private func quit() { task?.cancel(); NSApp.terminate(nil) }
    private func setBusy(_ value: Bool) { busy = value; actions.forEach { $0.isEnabled = !value }; menuActions.forEach { $0.isEnabled = !value } }
    private func append(_ message: String) { details.string += message + "\n"; details.scrollToEndOfDocument(nil) }
    private func showError(_ error: Error) { status.stringValue = error.localizedDescription; append(error.localizedDescription) }
    func numberOfRows(in tableView: NSTableView) -> Int { session.entries.count }
    func tableView(_ tableView: NSTableView, viewFor tableColumn: NSTableColumn?, row: Int) -> NSView? {
        guard row < session.entries.count else { return nil }; let entry = session.entries[row]
        let value: String
        switch tableColumn?.identifier.rawValue {
        case "order": value = String(row + 1)
        case "title": value = entry.title
        case "kind": value = [.app: "프로그램", .file: "문서", .folder: "폴더", .web: "웹 주소"][entry.kind] ?? ""
        case "app": value = entry.appName
        default: value = entry.note
        }
        let field = NSTextField(labelWithString: value); field.lineBreakMode = .byTruncatingTail; field.toolTip = value
        if tableColumn?.identifier.rawValue == "title" {
            let row = NSStackView(); row.orientation = .horizontal; row.spacing = 8
            let image = NSImageView(); image.image = NSWorkspace.shared.icon(forFile: entry.kind == .app || entry.kind == .web ? entry.appPath : (URL(string: entry.url)?.path ?? entry.appPath))
            image.imageScaling = .scaleProportionallyUpOrDown; image.widthAnchor.constraint(equalToConstant: 20).isActive = true; image.heightAnchor.constraint(equalToConstant: 20).isActive = true
            row.addArrangedSubview(image); row.addArrangedSubview(field); return row
        }
        return field
    }
    func tableViewSelectionDidChange(_ notification: Notification) {
        guard !busy, table.selectedRow >= 0, table.selectedRow < session.entries.count else { return }
        let entry = session.entries[table.selectedRow]
        details.string = "\(entry.title)\n\(entry.url.isEmpty ? entry.appPath : entry.url)\n\(entry.note)"
    }
}
