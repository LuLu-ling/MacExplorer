import AppKit
import Combine
import SwiftUI

public typealias MXIntCallback = @convention(c) (UnsafeMutableRawPointer?, Int32) -> Void
public typealias MXToggleCallback = @convention(c) (UnsafeMutableRawPointer?, Int32, Int32) -> Void
public typealias MXShortcutCallback = @convention(c) (UnsafeMutableRawPointer?, Int32, Int32, Int32) -> Void
public typealias MXFileTypeCallback = @convention(c) (UnsafeMutableRawPointer?, Int32, Int32, Int32, UnsafePointer<CChar>?) -> Void

struct MXSettingsPayload {
    var page: Int32
    var theme: Int32
    var languageIndex: Int32
    var flags: Int32
    var cardArgb: UInt32
    var strokeArgb: UInt32
    var pageTitles: UnsafePointer<CChar>?
    var themeLabel: UnsafePointer<CChar>?
    var themeOptions: UnsafePointer<CChar>?
    var languageLabel: UnsafePointer<CChar>?
    var languages: UnsafePointer<CChar>?
    var folderLabels: UnsafePointer<CChar>?
    var appName: UnsafePointer<CChar>?
    var version: UnsafePointer<CChar>?
    var description: UnsafePointer<CChar>?
    var categories: UnsafePointer<CChar>?
    var shortcutRows: UnsafePointer<CChar>?
    var shortcutLabels: UnsafePointer<CChar>?
    var fileTypes: UnsafePointer<CChar>?
    var fileTypeLabels: UnsafePointer<CChar>?
    var terminalLabel: UnsafePointer<CChar>?
    var terminalOptions: UnsafePointer<CChar>?
    var terminalIndex: Int32
}

@_cdecl("MXSettingsCreate")
public func MXSettingsCreate(
    _ context: UnsafeMutableRawPointer?,
    _ themeChanged: MXIntCallback?,
    _ languageChanged: MXIntCallback?,
    _ toggleChanged: MXToggleCallback?,
    _ shortcutChanged: MXShortcutCallback?,
    _ fileTypeChanged: MXFileTypeCallback?,
    _ terminalChanged: MXIntCallback?
) -> UnsafeMutableRawPointer {
    let view = SettingsView(frame: .zero)
    view.model.context = context
    view.model.themeChanged = themeChanged
    view.model.languageChanged = languageChanged
    view.model.toggleChanged = toggleChanged
    view.model.shortcutChanged = shortcutChanged
    view.model.fileTypeChanged = fileTypeChanged
    view.model.terminalChanged = terminalChanged
    return Unmanaged.passRetained(view).toOpaque()
}

@_cdecl("MXSettingsApply")
public func MXSettingsApply(_ view: UnsafeMutableRawPointer, _ data: UnsafeRawPointer) {
    Unmanaged<SettingsView>.fromOpaque(view).takeUnretainedValue().apply(
        data.assumingMemoryBound(to: MXSettingsPayload.self).pointee)
}

@_cdecl("MXSettingsRelease")
public func MXSettingsRelease(_ view: UnsafeMutableRawPointer) {
    Unmanaged<SettingsView>.fromOpaque(view).release()
}

private enum Metrics {
    static let heading: CGFloat = 26
    static let headingMin: CGFloat = 32
    static let rowHeight: CGFloat = 40
    static let rowInset: CGFloat = 14
    static let corner: CGFloat = 12
    static let folderCount = 5
    static let maxPage = 5
}

private struct ShortcutRow: Equatable, Identifiable {
    var id: Int
    var title: String
    var chord: String
    var custom: Bool
}

private struct ShortcutGroup: Equatable, Identifiable {
    var id: Int
    var title: String
    var rows: [ShortcutRow]
}

private struct FileTypeRow: Equatable, Identifiable {
    var id: String
    var name: String
    var ext: String
}

private struct SettingsState: Equatable {
    var page = 0
    var heading = ""
    var theme = 0
    var themeLabel = ""
    var themeOptions: [String] = []
    var languageIndex = 0
    var languageLabel = ""
    var languages: [String] = []
    var flags = 0
    var folderLabels = Array(repeating: "", count: Metrics.folderCount)
    var appName = ""
    var version = ""
    var description = ""
    var groups: [ShortcutGroup] = []
    var typePrompt = ""
    var noneLabel = ""
    var restoreAll = ""
    var restore = ""
    var fileTypes: [FileTypeRow] = []
    var addFileType = ""
    var fileTypeName = ""
    var fileTypeExt = ""
    var emptyFileTypes = ""
    var cardArgb: UInt32 = 0
    var strokeArgb: UInt32 = 0
    var terminalIndex = 0
    var terminalLabel = ""
    var terminalOptions: [String] = []

    init() {}

    init(_ data: MXSettingsPayload) {
        let titles = lines(data.pageTitles)
        let themeOptions = lines(data.themeOptions)
        let languages = lines(data.languages)
        var folderLabels = lines(data.folderLabels)
        folderLabels += Array(repeating: "", count: max(0, Metrics.folderCount - folderLabels.count))
        if folderLabels.count > Metrics.folderCount {
            folderLabels = Array(folderLabels.prefix(Metrics.folderCount))
        }
        page = min(max(Int(data.page), 0), Metrics.maxPage)
        heading = at(titles, page)
        theme = clamp(Int(data.theme), count: themeOptions.count)
        themeLabel = cString(data.themeLabel)
        self.themeOptions = themeOptions
        languageIndex = clamp(Int(data.languageIndex), count: languages.count)
        languageLabel = cString(data.languageLabel)
        self.languages = languages
        flags = Int(data.flags)
        self.folderLabels = folderLabels
        appName = cString(data.appName)
        version = cString(data.version)
        description = cString(data.description)
        let labels = lines(data.shortcutLabels)
        typePrompt = at(labels, 0)
        noneLabel = at(labels, 1)
        restoreAll = at(labels, 2)
        restore = at(labels, 3)
        groups = parseGroups(categories: lines(data.categories), rows: lines(data.shortcutRows))
        let typeLabels = lines(data.fileTypeLabels)
        addFileType = at(typeLabels, 0)
        fileTypeName = at(typeLabels, 1)
        fileTypeExt = at(typeLabels, 2)
        emptyFileTypes = at(typeLabels, 3)
        fileTypes = parseFileTypes(lines(data.fileTypes))
        let terminalOptions = lines(data.terminalOptions)
        terminalIndex = clamp(Int(data.terminalIndex), count: terminalOptions.count)
        terminalLabel = cString(data.terminalLabel)
        self.terminalOptions = terminalOptions
        cardArgb = data.cardArgb
        strokeArgb = data.strokeArgb
    }
}

private final class SettingsModel: ObservableObject {
    @Published private(set) var state = SettingsState()
    @Published private(set) var recordingId = -1

    var context: UnsafeMutableRawPointer?
    var themeChanged: MXIntCallback?
    var languageChanged: MXIntCallback?
    var toggleChanged: MXToggleCallback?
    var shortcutChanged: MXShortcutCallback?
    var fileTypeChanged: MXFileTypeCallback?
    var terminalChanged: MXIntCallback?

    private var applying = false
    private var monitors: [Any] = []
    fileprivate weak var recordingChip: NSView?

    deinit { endRecording() }

    func apply(_ data: MXSettingsPayload) {
        let next = SettingsState(data)
        if next.page != 4 { endRecording() }
        guard next != state else { return }
        applying = true
        defer { applying = false }
        var transaction = Transaction()
        transaction.disablesAnimations = true
        withTransaction(transaction) { state = next }
    }

    func setTheme(_ value: Int) {
        guard !applying, value != state.theme else { return }
        state.theme = value
        themeChanged?(context, Int32(value))
    }

    func setLanguage(_ value: Int) {
        guard !applying, value != state.languageIndex else { return }
        state.languageIndex = value
        languageChanged?(context, Int32(value))
    }

    func setTerminal(_ value: Int) {
        guard !applying, value != state.terminalIndex else { return }
        state.terminalIndex = value
        terminalChanged?(context, Int32(value))
    }
    func setFlag(_ index: Int, _ on: Bool) {
        guard !applying, (0..<Metrics.folderCount).contains(index) else { return }
        let bit = 1 << index
        let next = on ? state.flags | bit : state.flags & ~bit
        guard next != state.flags else { return }
        state.flags = next
        toggleChanged?(context, Int32(index), on ? 1 : 0)
    }

    func toggleRecording(_ id: Int) {
        if recordingId == id { return }
        beginRecording(id)
    }

    func reset(_ id: Int) {
        endRecording()
        shortcutChanged?(context, Int32(id), -2, 0)
    }

    func resetAll() {
        endRecording()
        shortcutChanged?(context, 0, -3, 0)
    }

    func addFileType() {
        sendFileType(0, 0, 0, nil)
    }

    func removeFileTypes(_ indices: IndexSet) {
        for index in indices.sorted(by: >) {
            sendFileType(1, index, 0, nil)
        }
    }

    func moveFileTypes(_ indices: IndexSet, _ dest: Int) {
        guard let from = indices.first else { return }
        sendFileType(2, from, dest, nil)
    }

    func setFileTypeName(_ id: String, _ name: String) {
        guard let index = state.fileTypes.firstIndex(where: { $0.id == id }) else { return }
        sendFileType(3, index, 0, name)
    }

    func setFileTypeExt(_ id: String, _ ext: String) {
        guard let index = state.fileTypes.firstIndex(where: { $0.id == id }) else { return }
        sendFileType(4, index, 0, ext)
    }

    private func sendFileType(_ op: Int, _ index: Int, _ dest: Int, _ text: String?) {
        if let text {
            text.withCString { fileTypeChanged?(context, Int32(op), Int32(index), Int32(dest), $0) }
        } else {
            fileTypeChanged?(context, Int32(op), Int32(index), Int32(dest), nil)
        }
    }

    private func beginRecording(_ id: Int) {
        endRecording()
        recordingId = id
        let mouse: NSEvent.EventTypeMask = [.leftMouseDown, .rightMouseDown, .otherMouseDown]
        if let local = NSEvent.addLocalMonitorForEvents(matching: mouse.union(.keyDown), handler: { [weak self] event in
            self?.handleLocal(event) ?? event
        }) {
            monitors.append(local)
        }
        if let global = NSEvent.addGlobalMonitorForEvents(matching: mouse, handler: { [weak self] _ in
            self?.endRecording()
        }) {
            monitors.append(global)
        }
    }

    func endRecording() {
        monitors.forEach { NSEvent.removeMonitor($0) }
        monitors.removeAll()
        recordingChip = nil
        if recordingId != -1 {
            recordingId = -1
        }
    }

    private func handleLocal(_ event: NSEvent) -> NSEvent? {
        if event.type == .keyDown {
            handleKey(event)
            return nil
        }
        if !clickIsOnChip(event) {
            let id = recordingId
            DispatchQueue.main.async { [weak self] in
                guard let self, self.recordingId == id else { return }
                self.endRecording()
            }
        }
        return event
    }
    private func clickIsOnChip(_ event: NSEvent) -> Bool {
        guard let chip = recordingChip, event.window === chip.window else { return false }
        if chip.bounds.isEmpty { return true }
        return chip.convert(chip.bounds, to: nil).insetBy(dx: -2, dy: -2).contains(event.locationInWindow)
    }

    private func handleKey(_ event: NSEvent) {
        if event.isARepeat { return }
        let keyCode = Int(event.keyCode)
        if (0x36...0x3F).contains(keyCode) { return }
        let flags = event.modifierFlags.intersection([.command, .shift, .option, .control])
        let id = recordingId
        endRecording()
        guard id >= 0 else { return }
        shortcutChanged?(context, Int32(id), Int32(keyCode), Int32(flags.rawValue))
    }
}

private struct SettingsPane: View {
    @ObservedObject var model: SettingsModel

    var body: some View {
        pane(model.state)
    }

    private func pane(_ state: SettingsState) -> some View {
        VStack(alignment: .leading, spacing: 0) {
            Text(state.heading)
                .font(.system(size: Metrics.heading, weight: .bold))
                .lineLimit(1)
                .frame(maxWidth: .infinity, minHeight: Metrics.headingMin, alignment: .leading)
            if state.page == 3 {
                newFiles(state)
                    .padding(.top, 12)
            } else if state.page == 4 {
                shortcuts(state)
                    .padding(.top, 12)
            } else {
                card(state)
                    .padding(.top, 12)
                if state.page == 5 {
                    Text(state.description)
                        .font(.system(size: 12))
                        .foregroundStyle(Color.secondary)
                        .frame(maxWidth: .infinity, alignment: .leading)
                        .padding(.top, 10)
                }
                Spacer(minLength: 8)
            }
        }
        .padding(.top, 10)
        .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .topLeading)
        .background(Color.clear)
        .ignoresSafeArea()
    }

    @ViewBuilder
    private func card(_ state: SettingsState) -> some View {
        let shape = RoundedRectangle(cornerRadius: Metrics.corner, style: .continuous)
        GlassEffectContainer {
            VStack(spacing: 0) {
                switch state.page {
                case 1:
                    picker(state.languageLabel, state.languages, language)
                case 2:
                    picker(state.terminalLabel, state.terminalOptions, terminal, divider: true)
                    ForEach(0..<Metrics.folderCount, id: \.self) { index in
                        toggle(state.folderLabels[index], flag(index), divider: index + 1 < Metrics.folderCount)
                    }
                case 5:
                    SettingsRow(title: state.appName) {
                        Text(state.version)
                            .font(.system(size: 13))
                            .foregroundStyle(Color.secondary)
                            .lineLimit(1)
                    }
                default:
                    picker(state.themeLabel, state.themeOptions, theme)
                }
            }
            .glassEffect(.regular, in: shape)
        }
    }

    private func newFiles(_ state: SettingsState) -> some View {
        VStack(alignment: .leading, spacing: 6) {
            let shape = RoundedRectangle(cornerRadius: Metrics.corner, style: .continuous)
            GlassEffectContainer {
                Group {
                    if state.fileTypes.isEmpty {
                        Text(state.emptyFileTypes)
                            .font(.system(size: 13))
                            .foregroundStyle(Color.secondary)
                            .multilineTextAlignment(.center)
                            .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .center)
                            .padding(.horizontal, Metrics.rowInset)
                    } else {
                        List {
                            ForEach(state.fileTypes) { row in
                                HStack(spacing: 8) {
                                    FileTypeEditor(
                                        row: row,
                                        nameLabel: state.fileTypeName,
                                        extLabel: state.fileTypeExt,
                                        onName: { model.setFileTypeName(row.id, $0) },
                                        onExt: { model.setFileTypeExt(row.id, $0) })
                                    Button {
                                        if let index = state.fileTypes.firstIndex(where: { $0.id == row.id }) {
                                            model.removeFileTypes(IndexSet(integer: index))
                                        }
                                    } label: {
                                        Image(systemName: "minus.circle.fill")
                                            .foregroundStyle(.red)
                                    }
                                    .buttonStyle(.plain)
                                }
                            }
                            .onMove(perform: model.moveFileTypes)
                            .onDelete(perform: model.removeFileTypes)
                        }
                        .listStyle(.plain)
                        .scrollContentBackground(.hidden)
                    }
                }
                .frame(maxWidth: .infinity, maxHeight: .infinity)
                .glassEffect(.regular, in: shape)
            }
            if !state.addFileType.isEmpty {
                Button(state.addFileType) { model.addFileType() }
                    .buttonStyle(.glass)
                    .controlSize(.regular)
            }
        }
        .padding(.bottom, 6)
        .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .topLeading)
    }

    private func shortcuts(_ state: SettingsState) -> some View {
        VStack(alignment: .leading, spacing: 6) {
            ScrollView {
                VStack(spacing: 12) {
                    ForEach(state.groups) { group in
                        shortcutGroup(group, state)
                    }
                }
            }
            if !state.restoreAll.isEmpty {
                Button(state.restoreAll) { model.resetAll() }
                    .buttonStyle(.glass)
                    .controlSize(.regular)
            }
        }
        .padding(.bottom, 6)
        .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .topLeading)
    }

    private func shortcutGroup(_ group: ShortcutGroup, _ state: SettingsState) -> some View {
        let shape = RoundedRectangle(cornerRadius: Metrics.corner, style: .continuous)
        return VStack(alignment: .leading, spacing: 6) {
            Text(group.title)
                .font(.system(size: 12, weight: .semibold))
                .foregroundStyle(Color.secondary)
                .padding(.leading, 4)
            GlassEffectContainer {
                VStack(spacing: 0) {
                    ForEach(Array(group.rows.enumerated()), id: \.element.id) { index, row in
                        shortcutRow(row, state, divider: index + 1 < group.rows.count)
                    }
                }
                .glassEffect(.regular, in: shape)
            }
        }
    }

    private func shortcutRow(_ row: ShortcutRow, _ state: SettingsState, divider: Bool) -> some View {
        let recording = model.recordingId == row.id
        return SettingsRow(title: row.title, divider: divider) {
            Button {
                model.toggleRecording(row.id)
            } label: {
                Text(recording ? state.typePrompt : (row.chord.isEmpty ? state.noneLabel : row.chord))
                    .font(.system(size: 13, weight: .medium, design: .rounded))
                    .foregroundStyle(recording ? Color.accentColor : (row.chord.isEmpty ? Color.secondary : Color.primary))
                    .lineLimit(1)
                    .padding(.horizontal, 8)
                    .frame(minWidth: 36, minHeight: 22)
                    .background(
                        RoundedRectangle(cornerRadius: 6, style: .continuous)
                            .fill(Color.primary.opacity(recording ? 0.12 : 0.06)))
                    .overlay {
                        RoundedRectangle(cornerRadius: 6, style: .continuous)
                            .strokeBorder(recording ? Color.accentColor : Color.clear, lineWidth: 1)
                        if recording {
                            ChipProbe(model: model)
                                .frame(maxWidth: .infinity, maxHeight: .infinity)
                                .allowsHitTesting(false)
                        }
                    }
            }
            .buttonStyle(.plain)
            .contextMenu {
                Button(state.restore) { model.reset(row.id) }
                    .disabled(!row.custom)
            }
        }
    }

    private func picker(_ title: String, _ options: [String], _ selection: Binding<Int>, divider: Bool = false) -> some View {
        SettingsRow(title: title, divider: divider) {
            Picker(title, selection: selection) {
                ForEach(options.indices, id: \.self) { index in
                    Text(options[index]).tag(index)
                }
            }
            .pickerStyle(.menu)
            .buttonStyle(.glass)
            .labelsHidden()
            .fixedSize()
            .controlSize(.regular)
            .disabled(options.isEmpty)
        }
    }

    private func toggle(_ title: String, _ isOn: Binding<Bool>, divider: Bool) -> some View {
        SettingsRow(title: title, divider: divider) {
            Toggle(title, isOn: isOn)
                .toggleStyle(.switch)
                .labelsHidden()
                .controlSize(.regular)
        }
    }

    private var theme: Binding<Int> {
        Binding(get: { model.state.theme }, set: { model.setTheme($0) })
    }

    private var language: Binding<Int> {
        Binding(get: { model.state.languageIndex }, set: { model.setLanguage($0) })
    }

    private var terminal: Binding<Int> {
        Binding(get: { model.state.terminalIndex }, set: { model.setTerminal($0) })
    }

    private func flag(_ index: Int) -> Binding<Bool> {
        Binding(
            get: { (model.state.flags & (1 << index)) != 0 },
            set: { model.setFlag(index, $0) })
    }
}

private struct SettingsRow<Accessory: View>: View {
    let title: String
    let divider: Bool
    let accessory: Accessory

    init(title: String, divider: Bool = false, @ViewBuilder accessory: () -> Accessory) {
        self.title = title
        self.divider = divider
        self.accessory = accessory()
    }

    var body: some View {
        VStack(spacing: 0) {
            HStack(spacing: 12) {
                Text(title)
                    .font(.system(size: 13))
                    .lineLimit(1)
                    .truncationMode(.tail)
                    .frame(maxWidth: .infinity, alignment: .leading)
                accessory
            }
            .padding(.horizontal, Metrics.rowInset)
            .frame(height: Metrics.rowHeight)
            if divider {
                Divider().padding(.leading, Metrics.rowInset)
            }
        }
    }
}

private struct FileTypeEditor: View {
    enum Field { case name, ext }

    let row: FileTypeRow
    let nameLabel: String
    let extLabel: String
    let onName: (String) -> Void
    let onExt: (String) -> Void
    @State private var name: String
    @State private var ext: String
    @FocusState private var focus: Field?

    init(row: FileTypeRow, nameLabel: String, extLabel: String, onName: @escaping (String) -> Void, onExt: @escaping (String) -> Void) {
        self.row = row
        self.nameLabel = nameLabel
        self.extLabel = extLabel
        self.onName = onName
        self.onExt = onExt
        _name = State(initialValue: row.name)
        _ext = State(initialValue: row.ext)
    }

    var body: some View {
        HStack(spacing: 8) {
            TextField(nameLabel, text: $name)
                .textFieldStyle(.roundedBorder)
                .focused($focus, equals: .name)
                .onSubmit { onName(name) }
            Text(".")
                .foregroundStyle(Color.secondary)
            TextField(extLabel, text: $ext)
                .textFieldStyle(.roundedBorder)
                .frame(width: 72)
                .focused($focus, equals: .ext)
                .onSubmit { onExt(ext) }
        }
        .onChange(of: focus) { old, value in
            if old == .name && value != .name { onName(name) }
            if old == .ext && value != .ext { onExt(ext) }
        }
        .onChange(of: row.name) { _, value in
            if focus != .name { name = value }
        }
        .onChange(of: row.ext) { _, value in
            if focus != .ext { ext = value }
        }
    }
}


private final class HostingView: NSHostingView<SettingsPane> {
    override var isOpaque: Bool { false }

    required init(rootView: SettingsPane) {
        super.init(rootView: rootView)
        sizingOptions = []
        safeAreaRegions = []
        translatesAutoresizingMaskIntoConstraints = true
        autoresizingMask = [.width, .height]
        layer?.isOpaque = false
        layer?.backgroundColor = NSColor.clear.cgColor
    }

    @available(*, unavailable)
    required init?(coder: NSCoder) { nil }
}

private final class SettingsView: NSView {
    let model = SettingsModel()
    private let host: HostingView

    override var isOpaque: Bool { false }

    override init(frame frameRect: NSRect) {
        host = HostingView(rootView: SettingsPane(model: model))
        super.init(frame: frameRect)
        autoresizingMask = [.width, .height]
        host.frame = bounds
        addSubview(host)
    }

    required init?(coder: NSCoder) { nil }

    func apply(_ data: MXSettingsPayload) {
        model.apply(data)
    }

    override func setFrameSize(_ newSize: NSSize) {
        super.setFrameSize(newSize)
        host.frame = bounds
    }

    override func layout() {
        super.layout()
        host.frame = bounds
    }
}

private struct ChipProbe: NSViewRepresentable {
    let model: SettingsModel

    func makeNSView(context: Context) -> Probe {
        let view = Probe()
        view.model = model
        model.recordingChip = view
        return view
    }

    func updateNSView(_ view: Probe, context: Context) {
        view.model = model
        model.recordingChip = view
    }

    final class Probe: NSView {
        weak var model: SettingsModel?
        override func hitTest(_ point: NSPoint) -> NSView? { nil }
        override func viewDidMoveToWindow() {
            super.viewDidMoveToWindow()
            model?.recordingChip = window == nil ? nil : self
        }
    }
}

private func cString(_ ptr: UnsafePointer<CChar>?) -> String {
    ptr.map { String(cString: $0) } ?? ""
}

private func lines(_ ptr: UnsafePointer<CChar>?) -> [String] {
    let raw = cString(ptr)
    return raw.isEmpty ? [] : raw.split(separator: "\n", omittingEmptySubsequences: false).map(String.init)
}

private func at(_ items: [String], _ index: Int) -> String {
    items.indices.contains(index) ? items[index] : ""
}

private func clamp(_ value: Int, count: Int) -> Int {
    guard count > 0 else { return 0 }
    return min(max(value, 0), count - 1)
}

private func parseGroups(categories: [String], rows: [String]) -> [ShortcutGroup] {
    var buckets = Array(repeating: [ShortcutRow](), count: max(categories.count, 1))
    for line in rows where !line.isEmpty {
        let parts = line.split(separator: "\t", omittingEmptySubsequences: false).map(String.init)
        guard parts.count >= 5, let id = Int(parts[0]), let cat = Int(parts[1]) else { continue }
        let row = ShortcutRow(id: id, title: parts[2], chord: parts[3], custom: parts[4] != "0")
        if buckets.indices.contains(cat) {
            buckets[cat].append(row)
        }
    }
    return zip(categories.indices, categories).compactMap { index, title in
        guard buckets.indices.contains(index), !buckets[index].isEmpty else { return nil }
        return ShortcutGroup(id: index, title: title, rows: buckets[index])
    }
}

private func parseFileTypes(_ rows: [String]) -> [FileTypeRow] {
    rows.compactMap { line in
        if line.isEmpty { return nil }
        let parts = line.split(separator: "\t", omittingEmptySubsequences: false).map(String.init)
        guard parts.count >= 3 else { return nil }
        return FileTypeRow(id: parts[0], name: parts[1], ext: parts[2])
    }
}
