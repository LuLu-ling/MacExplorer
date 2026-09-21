import AppKit
import SwiftUI
import UniformTypeIdentifiers

public typealias MXStringCallback = @convention(c) (UnsafeMutableRawPointer?, UnsafePointer<CChar>?) -> Void
public typealias MXMoveCallback = @convention(c) (UnsafeMutableRawPointer?, Int32, Int32) -> Void
public typealias MXDropCallback = @convention(c) (UnsafeMutableRawPointer?, UnsafePointer<CChar>?, UnsafePointer<CChar>?, Int32) -> Void
public typealias MXRenameCallback = @convention(c) (UnsafeMutableRawPointer?, UnsafePointer<CChar>?, UnsafePointer<CChar>?) -> Void
public typealias MXVoidCallback = @convention(c) (UnsafeMutableRawPointer?) -> Void

struct MXSidebarPayload {
    var selectArgb: UInt32
    var hoverArgb: UInt32
    var accentArgb: UInt32
    var secondaryArgb: UInt32
    var rows: UnsafePointer<CChar>?
    var footerTitle: UnsafePointer<CChar>?
    var renamingId: UnsafePointer<CChar>?
    var renameText: UnsafePointer<CChar>?
}

@_cdecl("MXSidebarCreate")
public func MXSidebarCreate(
    _ context: UnsafeMutableRawPointer?,
    _ select: MXStringCallback?,
    _ move: MXMoveCallback?,
    _ contextMenu: MXStringCallback?,
    _ drop: MXDropCallback?,
    _ hover: MXStringCallback?,
    _ rename: MXRenameCallback?,
    _ settings: MXVoidCallback?
) -> UnsafeMutableRawPointer {
    let view = SidebarHostView(frame: .zero)
    view.model.context = context
    view.model.select = select
    view.model.move = move
    view.model.contextMenu = contextMenu
    view.model.drop = drop
    view.model.hover = hover
    view.model.rename = rename
    view.model.settings = settings
    return Unmanaged.passRetained(view).toOpaque()
}

@_cdecl("MXSidebarApply")
public func MXSidebarApply(_ view: UnsafeMutableRawPointer, _ data: UnsafeRawPointer) {
    Unmanaged<SidebarHostView>.fromOpaque(view).takeUnretainedValue().apply(
        data.assumingMemoryBound(to: MXSidebarPayload.self).pointee)
}

@_cdecl("MXSidebarRelease")
public func MXSidebarRelease(_ view: UnsafeMutableRawPointer) {
    Unmanaged<SidebarHostView>.fromOpaque(view).release()
}

@_cdecl("MXSidebarWarmup")
public func MXSidebarWarmup() {
    let frame = NSRect(x: 0, y: 0, width: 240, height: 640)
    let view = SidebarHostView(frame: frame)
    let window = NSWindow(
        contentRect: NSRect(x: -8000, y: -8000, width: 240, height: 640),
        styleMask: .borderless,
        backing: .buffered,
        defer: false)
    window.isReleasedWhenClosed = false
    window.alphaValue = 0
    window.ignoresMouseEvents = true
    window.contentView = view
    view.attachHostIfReady(reveal: true)
    view.layoutSubtreeIfNeeded()
    view.displayIfNeeded()
    window.contentView = nil
    window.close()
}

private enum RowFlag {
    static let selected = 1
    static let section = 2
    static let expanded = 4
    static let marker = 8
    static let reorder = 16
    static let collapsed = 32
}

private enum Metrics {
    static let item: CGFloat = 34
    static let section: CGFloat = 40

    static func height(_ row: SidebarRow) -> CGFloat {
        row.collapsed ? 0 : (row.section ? section : item)
    }
}

private enum Motion {
    static let shift = Animation.timingCurve(0.22, 1, 0.36, 1, duration: 0.22)
    static let collapse = Animation.easeOut(duration: 0.22)
    static let theme = Animation.easeInOut(duration: 0.3)
}

private struct SidebarRow: Equatable, Identifiable {
    var id: String
    var title: String
    var symbol: String
    var flags: Int
    var argb: UInt32

    var selected: Bool { flags & RowFlag.selected != 0 }
    var section: Bool { flags & RowFlag.section != 0 }
    var expanded: Bool { flags & RowFlag.expanded != 0 }
    var hasMarker: Bool { flags & RowFlag.marker != 0 }
    var reorder: Bool { flags & RowFlag.reorder != 0 }
    var collapsed: Bool { flags & RowFlag.collapsed != 0 }
    var itemReorder: Bool { reorder && !section && id != "home" }
    var blockStart: Bool { section || id == "home" }
}

private struct SidebarState: Equatable {
    var rows: [SidebarRow] = []
    var footerTitle = ""
    var footerSelected = false
    var renamingId = ""
    var renameText = ""
    var selectArgb: UInt32 = 0
    var hoverArgb: UInt32 = 0
    var accentArgb: UInt32 = 0
    var secondaryArgb: UInt32 = 0

    init() {}

    init(_ data: MXSidebarPayload) {
        rows = parseRows(lines(data.rows))
        footerTitle = cString(data.footerTitle)
        renamingId = cString(data.renamingId)
        renameText = cString(data.renameText)
        selectArgb = data.selectArgb
        hoverArgb = data.hoverArgb
        accentArgb = data.accentArgb
        secondaryArgb = data.secondaryArgb
        footerSelected = false
        if let index = rows.firstIndex(where: { $0.id == "settings-footer" }) {
            footerSelected = rows[index].selected
            rows.remove(at: index)
        }
    }
}

private enum Reorder {
    struct Run {
        var start: Int
        var count: Int
        var end: Int { start + count }
    }

    struct Layout {
        var y: [CGFloat]
        var h: [CGFloat]

        static func of(_ rows: [SidebarRow]) -> Layout {
            var y = [CGFloat](repeating: 0, count: rows.count)
            var h = [CGFloat](repeating: 0, count: rows.count)
            var acc: CGFloat = 0
            for i in rows.indices {
                h[i] = Metrics.height(rows[i])
                y[i] = acc
                acc += h[i]
            }
            return Layout(y: y, h: h)
        }

        func minY(_ run: Run) -> CGFloat {
            y.indices.contains(run.start) ? y[run.start] : 0
        }

        func slot(_ run: Run) -> CGFloat {
            guard y.indices.contains(run.start) else { return 0 }
            let last = run.end - 1
            guard y.indices.contains(last) else { return h[run.start] }
            return y[last] + h[last] - y[run.start]
        }
    }

    static func units(in rows: [SidebarRow], at index: Int) -> [Run]? {
        guard rows.indices.contains(index) else { return nil }
        let row = rows[index]
        if row.blockStart { return blocks(in: rows) }
        guard row.itemReorder else { return nil }
        var lo = index, hi = index
        while lo > 0, rows[lo - 1].itemReorder { lo -= 1 }
        while hi + 1 < rows.count, rows[hi + 1].itemReorder { hi += 1 }
        guard hi > lo else { return nil }
        return (lo...hi).map { Run(start: $0, count: 1) }
    }

    static func blocks(in rows: [SidebarRow]) -> [Run] {
        var units: [Run] = []
        var i = 0
        while i < rows.count {
            let start = i
            i += 1
            while i < rows.count, !rows[i].blockStart { i += 1 }
            units.append(Run(start: start, count: i - start))
        }
        return units
    }

    static func index(of item: Int, in runs: [Run]) -> Int? {
        runs.firstIndex { item >= $0.start && item < $0.end }
    }

    static func clamped(translation: CGFloat, layout: Layout, runs: [Run], fromRun: Int, slot: CGFloat) -> CGFloat {
        guard runs.indices.contains(fromRun), let last = runs.last else { return translation }
        let origin = layout.minY(runs[fromRun])
        let minDelta = layout.minY(runs[0]) - origin
        let maxDelta = layout.minY(last) + layout.slot(last) - origin - slot
        return min(max(translation, minDelta), maxDelta)
    }

    static func hoverRun(fromRun: Int, center: CGFloat, runs: [Run], layout: Layout) -> Int {
        let n = runs.count
        guard runs.indices.contains(fromRun) else { return fromRun }
        var mids = [CGFloat](repeating: 0, count: n)
        var sizes = [CGFloat](repeating: 0, count: n)
        for i in 0..<n {
            sizes[i] = layout.slot(runs[i])
            let pos = layout.minY(runs[i])
            mids[i] = sizes[i] < 1 ? pos : pos + sizes[i] / 2
        }

        var hover = fromRun
        var prev = fromRun
        var i = fromRun + 1
        while i < n {
            if sizes[i] >= 1 {
                if center >= (mids[prev] + mids[i]) / 2 {
                    hover = i
                    prev = i
                } else {
                    break
                }
            }
            i += 1
        }

        prev = fromRun
        i = fromRun - 1
        while i >= 0 {
            if sizes[i] >= 1 {
                if center <= (mids[i] + mids[prev]) / 2 {
                    hover = i
                    prev = i
                } else {
                    break
                }
            }
            i -= 1
        }
        return hover
    }

    static func shifts(
        rows: [SidebarRow],
        fromRun: Int,
        hoverRun: Int,
        runs: [Run],
        slot: CGFloat
    ) -> [String: CGFloat] {
        guard runs.indices.contains(fromRun), runs.indices.contains(hoverRun) else { return [:] }
        let fromStart = runs[fromRun].start
        let fromEnd = runs[fromRun].end
        let hoverStart = runs[hoverRun].start
        let hoverEnd = runs[hoverRun].end
        var result: [String: CGFloat] = [:]
        for (i, row) in rows.enumerated() {
            if i >= fromStart && i < fromEnd { continue }
            var shift: CGFloat = 0
            if fromRun < hoverRun && i >= fromEnd && i < hoverEnd {
                shift = -slot
            } else if fromRun > hoverRun && i >= hoverStart && i < fromStart {
                shift = slot
            }
            if shift != 0 { result[row.id] = shift }
        }
        return result
    }
}

private final class SidebarModel: ObservableObject {
    @Published private(set) var state = SidebarState()
    @Published var draft = ""
    @Published var hoverId = ""
    @Published var draggingId = ""
    @Published var dragOffset: CGFloat = 0
    @Published var shifts: [String: CGFloat] = [:]

    var context: UnsafeMutableRawPointer?
    var select: MXStringCallback?
    var move: MXMoveCallback?
    var contextMenu: MXStringCallback?
    var drop: MXDropCallback?
    var hover: MXStringCallback?
    var rename: MXRenameCallback?
    var settings: MXVoidCallback?

    private var dragLayout: Reorder.Layout?
    private var dragFrom = -1
    private var dragFromRun = -1
    private var dragHoverRun = -1
    private var dragRuns: [Reorder.Run]?
    private var draggedIds: Set<String> = []
    private var didDrag = false

    func apply(_ data: MXSidebarPayload) {
        let next = SidebarState(data)
        guard next != state else { return }
        var transaction = Transaction()
        if Self.layoutChange(from: state, to: next) {
            transaction.animation = Motion.collapse
        } else if Self.paletteChange(from: state, to: next) {
            transaction.animation = Motion.theme
        } else {
            transaction.disablesAnimations = true
        }
        withTransaction(transaction) {
            if next.renamingId != state.renamingId {
                draft = next.renameText
            }
            state = next
            if !draggingId.isEmpty, !next.rows.contains(where: { $0.id == draggingId }) {
                resetDrag()
            }
        }
    }

    func tap(_ id: String) {
        if didDrag || !draggingId.isEmpty { return }
        send(select, id)
    }

    func showMenu(_ id: String) {
        send(contextMenu, id)
    }

    func setPointerHover(_ id: String, _ on: Bool) {
        if !draggingId.isEmpty { return }
        let next = on ? id : (hoverId == id ? "" : hoverId)
        if hoverId != next { hoverId = next }
    }

    func setDropHover(_ id: String, _ on: Bool) {
        let next = on ? id : ""
        if hoverId != next { hoverId = next }
        send(hover, next)
    }

    func dropFiles(_ id: String, _ providers: [NSItemProvider]) -> Bool {
        loadPaths(providers) { [weak self] paths in
            guard let self, !paths.isEmpty else { return }
            let blob = paths.joined(separator: "\n")
            let mods = Int32(NSEvent.modifierFlags.intersection([.command, .shift, .option, .control]).rawValue)
            id.withCString { idPtr in
                blob.withCString { pathPtr in
                    self.drop?(self.context, idPtr, pathPtr, mods)
                }
            }
        }
        return true
    }

    func dragChanged(_ row: SidebarRow, _ value: DragGesture.Value) {
        if fileDragActive { return }
        if draggingId.isEmpty {
            guard row.reorder, abs(value.translation.height) >= 6 else { return }
            guard let index = state.rows.firstIndex(where: { $0.id == row.id }),
                  let units = Reorder.units(in: state.rows, at: index),
                  units.count > 1,
                  let fromRun = Reorder.index(of: index, in: units) else { return }
            draggingId = row.id
            dragFrom = index
            dragRuns = units
            dragFromRun = fromRun
            dragHoverRun = fromRun
            dragLayout = Reorder.Layout.of(state.rows)
            draggedIds = ids(in: units[fromRun])
            didDrag = true
            hoverId = ""
        }
        guard draggingId == row.id,
              let runs = dragRuns,
              let layout = dragLayout,
              dragFromRun >= 0 else { return }
        let from = runs[dragFromRun]
        let slot = layout.slot(from)
        let offset = Reorder.clamped(
            translation: value.translation.height,
            layout: layout,
            runs: runs,
            fromRun: dragFromRun,
            slot: slot)
        if dragOffset != offset { dragOffset = offset }
        let hover = Reorder.hoverRun(
            fromRun: dragFromRun,
            center: layout.minY(from) + slot / 2 + offset,
            runs: runs,
            layout: layout)
        if hover == dragHoverRun { return }
        dragHoverRun = hover
        shifts = Reorder.shifts(
            rows: state.rows, fromRun: dragFromRun, hoverRun: hover, runs: runs, slot: slot)
    }

    func dragEnded() {
        let fromIndex = dragFrom
        let fromRun = dragFromRun
        let toRun = dragHoverRun
        let runs = dragRuns
        let dragged = didDrag
        var transaction = Transaction()
        transaction.disablesAnimations = true
        withTransaction(transaction) { resetDrag() }
        DispatchQueue.main.async { [weak self] in self?.didDrag = false }
        guard dragged, let runs, fromRun != toRun,
              runs.indices.contains(fromRun), runs.indices.contains(toRun),
              fromIndex >= 0 else { return }
        move?(context, Int32(fromIndex), Int32(runs[toRun].start))
    }

    func commitRename() {
        let id = state.renamingId
        guard !id.isEmpty else { return }
        sendRename(id, draft)
    }

    func cancelRename() {
        let id = state.renamingId
        guard !id.isEmpty else { return }
        sendRename(id, "")
    }

    func openSettings() {
        settings?(context)
    }

    func offset(for id: String) -> CGFloat {
        draggedIds.contains(id) ? dragOffset : (shifts[id] ?? 0)
    }

    func isDragging(_ id: String) -> Bool {
        draggedIds.contains(id)
    }

    private func resetDrag() {
        draggingId = ""
        dragOffset = 0
        shifts = [:]
        dragFrom = -1
        dragFromRun = -1
        dragHoverRun = -1
        dragRuns = nil
        dragLayout = nil
        draggedIds = []
    }

    private func ids(in run: Reorder.Run) -> Set<String> {
        var result: Set<String> = []
        let last = min(run.end, state.rows.count)
        var i = run.start
        while i < last {
            result.insert(state.rows[i].id)
            i += 1
        }
        return result
    }

    private static func layoutChange(from: SidebarState, to: SidebarState) -> Bool {
        guard from.rows.count == to.rows.count else { return false }
        var changed = false
        for (a, b) in zip(from.rows, to.rows) {
            if a.id != b.id { return false }
            if a.collapsed != b.collapsed || a.expanded != b.expanded { changed = true }
        }
        return changed
    }

    private static func paletteChange(from: SidebarState, to: SidebarState) -> Bool {
        guard from.rows.count == to.rows.count else { return false }
        for (a, b) in zip(from.rows, to.rows) {
            if a.id != b.id || a.flags != b.flags || a.title != b.title || a.symbol != b.symbol { return false }
        }
        return from.selectArgb != to.selectArgb
            || from.hoverArgb != to.hoverArgb
            || from.accentArgb != to.accentArgb
            || from.secondaryArgb != to.secondaryArgb
    }

    private var fileDragActive: Bool {
        NSPasteboard(name: .drag).availableType(from: [.fileURL]) != nil
    }

    private func send(_ callback: MXStringCallback?, _ value: String) {
        value.withCString { callback?(context, $0) }
    }

    private func sendRename(_ id: String, _ text: String) {
        id.withCString { idPtr in
            text.withCString { textPtr in
                rename?(context, idPtr, textPtr)
            }
        }
    }
}

private struct SidebarPane: View {
    @ObservedObject var model: SidebarModel
    @FocusState private var renameFocus: Bool

    var body: some View {
        let state = model.state
        let shape = RoundedRectangle(cornerRadius: 12, style: .continuous)
        GlassEffectContainer {
            VStack(spacing: 0) {
                ScrollView {
                    VStack(spacing: 0) {
                        ForEach(state.rows) { row in
                            slot(row, state)
                        }
                    }
                    .padding(.vertical, 6)
                    .coordinateSpace(name: "sidebar")
                }
                .scrollDisabled(!model.draggingId.isEmpty)
                if !state.footerTitle.isEmpty {
                    Divider()
                        .padding(.horizontal, 16)
                    footer(state)
                        .padding(.top, 6)
                        .padding(.bottom, 6)
                }
            }
            .glassEffect(.regular, in: shape)
        }
        .frame(maxWidth: .infinity, maxHeight: .infinity)
        .background(Color.clear)
    }

    private func slot(_ row: SidebarRow, _ state: SidebarState) -> some View {
        let collapsed = row.collapsed
        let dragging = model.isDragging(row.id)
        return rowView(row, state)
            .padding(.horizontal, 8)
            .frame(height: collapsed ? 0 : (row.section ? 28 : 32), alignment: .top)
            .padding(.top, collapsed ? 0 : (row.section ? 10 : 1))
            .padding(.bottom, collapsed ? 0 : (row.section ? 2 : 1))
            .opacity(collapsed ? 0 : (dragging ? 0.92 : 1))
            .animation(Motion.collapse, value: collapsed)
            .clipped()
            .allowsHitTesting(!collapsed)
            .offset(y: model.offset(for: row.id))
            .animation(dragging ? nil : Motion.shift, value: model.offset(for: row.id))
            .zIndex(dragging ? 1 : 0)
            .onHover { hovering in
                if !row.section { model.setPointerHover(row.id, hovering) }
            }
    }

    @ViewBuilder
    private func rowView(_ row: SidebarRow, _ state: SidebarState) -> some View {
        let renaming = row.id == state.renamingId
        let selected = row.selected && !row.section
        Button {
            if model.draggingId.isEmpty {
                model.tap(row.id)
            }
        } label: {
            HStack(spacing: 8) {
                if row.section {
                    Image(systemName: "chevron.right")
                        .font(.system(size: 10, weight: .semibold))
                        .foregroundStyle(Color(argb: state.secondaryArgb))
                        .frame(width: 12)
                        .rotationEffect(.degrees(row.expanded ? 90 : 0))
                }
                if row.hasMarker {
                    Circle()
                        .fill(Color(argb: row.argb))
                        .frame(width: 12, height: 12)
                } else if !row.symbol.isEmpty && !row.section {
                    Image(systemName: row.symbol)
                        .font(.system(size: 13, weight: .medium))
                        .foregroundStyle(selected ? Color(argb: state.accentArgb) : Color.primary)
                        .frame(width: 16)
                }
                if renaming {
                    TextField("", text: $model.draft)
                        .textFieldStyle(.plain)
                        .font(.system(size: 13))
                        .focused($renameFocus)
                        .onSubmit { model.commitRename() }
                        .onExitCommand { model.cancelRename() }
                        .onAppear { renameFocus = true }
                } else {
                    Text(row.title)
                        .font(row.section ? .system(size: 11, weight: .semibold) : .system(size: 13))
                        .foregroundStyle(selected
                                         ? Color(argb: state.accentArgb)
                                         : (row.section ? Color(argb: state.secondaryArgb) : Color.primary))
                        .lineLimit(1)
                        .truncationMode(.tail)
                }
                Spacer(minLength: 0)
            }
            .padding(.horizontal, 8)
            .frame(maxWidth: .infinity, minHeight: row.section ? 28 : 32, alignment: .leading)
            .background(
                RoundedRectangle(cornerRadius: 10, style: .continuous)
                    .fill(fill(row, state, selected))
                    .animation(nil, value: model.hoverId))
            .contentShape(Rectangle())
        }
        .buttonStyle(.plain)
        .simultaneousGesture(dragGesture(row))
        .background(RightClick { model.showMenu(row.id) })
        .onDrop(of: [UTType.fileURL], isTargeted: dropBinding(row.id)) { providers in
            model.dropFiles(row.id, providers)
        }
    }

    private func footer(_ state: SidebarState) -> some View {
        let hovered = model.hoverId == "settings-footer"
        return Button(action: model.openSettings) {
            HStack(spacing: 8) {
                Image(systemName: "gearshape")
                    .font(.system(size: 13, weight: .medium))
                    .foregroundStyle(state.footerSelected ? Color(argb: state.accentArgb) : Color.primary)
                    .frame(width: 16)
                Text(state.footerTitle)
                    .font(.system(size: 13))
                    .foregroundStyle(state.footerSelected ? Color(argb: state.accentArgb) : Color.primary)
                    .lineLimit(1)
                Spacer(minLength: 0)
            }
            .padding(.horizontal, 8)
            .frame(maxWidth: .infinity, minHeight: 32, alignment: .leading)
            .background(
                RoundedRectangle(cornerRadius: 10, style: .continuous)
                    .fill(state.footerSelected
                          ? Color(argb: state.selectArgb)
                          : (hovered ? Color(argb: state.hoverArgb) : Color.clear))
                    .animation(nil, value: model.hoverId))
            .contentShape(Rectangle())
        }
        .buttonStyle(.plain)
        .onHover { model.setPointerHover("settings-footer", $0) }
        .padding(.horizontal, 8)
    }

    private func fill(_ row: SidebarRow, _ state: SidebarState, _ selected: Bool) -> Color {
        if row.section { return .clear }
        if selected { return Color(argb: state.selectArgb) }
        if model.hoverId == row.id { return Color(argb: state.hoverArgb) }
        return .clear
    }

    private func dragGesture(_ row: SidebarRow) -> some Gesture {
        DragGesture(minimumDistance: 6, coordinateSpace: .named("sidebar"))
            .onChanged { model.dragChanged(row, $0) }
            .onEnded { _ in model.dragEnded() }
    }

    private func dropBinding(_ id: String) -> Binding<Bool> {
        Binding(
            get: { false },
            set: { model.setDropHover(id, $0) })
    }
}

private final class HostingView: NSHostingView<SidebarPane> {
    override var isOpaque: Bool { false }

    required init(rootView: SidebarPane) {
        super.init(rootView: rootView)
        sizingOptions = []
        safeAreaRegions = []
        translatesAutoresizingMaskIntoConstraints = true
        autoresizingMask = [.width, .height]
    }

    @available(*, unavailable)
    required init?(coder: NSCoder) { nil }
}

private final class SidebarHostView: NSView {
    let model = SidebarModel()
    private let host: HostingView

    override var isOpaque: Bool { false }
    override var isFlipped: Bool { true }

    override init(frame frameRect: NSRect) {
        host = HostingView(rootView: SidebarPane(model: model))
        super.init(frame: frameRect)
        autoresizingMask = [.width, .height]
        host.autoresizingMask = [.width, .height]
        host.isHidden = true
    }

    required init?(coder: NSCoder) { nil }

    func apply(_ data: MXSidebarPayload) {
        model.apply(data)
        attachHostIfReady()
    }

    override func viewDidMoveToWindow() {
        super.viewDidMoveToWindow()
        attachHostIfReady()
    }

    override func setFrameSize(_ newSize: NSSize) {
        super.setFrameSize(newSize)
        attachHostIfReady()
    }

    override func layout() {
        super.layout()
        attachHostIfReady()
    }

    fileprivate func attachHostIfReady(reveal: Bool = false) {
        let size = bounds.size
        guard window != nil, size.width > 1, size.height > 1 else { return }
        host.frame = CGRect(origin: .zero, size: size)
        if host.superview == nil {
            addSubview(host)
            layoutSubtreeIfNeeded()
            displayIfNeeded()
            if reveal {
                host.isHidden = false
            } else {
                DispatchQueue.main.async { [weak self] in
                    self?.host.isHidden = false
                }
            }
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

private func parseRows(_ rows: [String]) -> [SidebarRow] {
    rows.compactMap { line in
        if line.isEmpty { return nil }
        let parts = line.split(separator: "\t", omittingEmptySubsequences: false).map(String.init)
        guard parts.count >= 5, let flags = Int(parts[3]) else { return nil }
        let argb = UInt32(parts[4], radix: 16) ?? 0
        return SidebarRow(id: parts[0], title: parts[1], symbol: parts[2], flags: flags, argb: argb)
    }
}

private func loadPaths(_ providers: [NSItemProvider], done: @escaping ([String]) -> Void) {
    let group = DispatchGroup()
    let lock = NSLock()
    var paths: [String] = []
    for provider in providers {
        group.enter()
        provider.loadItem(forTypeIdentifier: UTType.fileURL.identifier, options: nil) { item, _ in
            defer { group.leave() }
            let url: URL?
            if let data = item as? Data {
                url = URL(dataRepresentation: data, relativeTo: nil)
            } else {
                url = item as? URL
            }
            guard let url, url.isFileURL else { return }
            lock.lock()
            paths.append(url.path)
            lock.unlock()
        }
    }
    group.notify(queue: .main) { done(paths) }
}

private struct RightClick: NSViewRepresentable {
    var action: () -> Void

    func makeNSView(context: Context) -> Catcher {
        let view = Catcher()
        view.action = action
        return view
    }

    func updateNSView(_ view: Catcher, context: Context) {
        view.action = action
    }

    final class Catcher: NSView {
        var action: () -> Void = {}
        private var monitor: Any?

        override func hitTest(_ point: NSPoint) -> NSView? { nil }

        override func viewDidMoveToWindow() {
            super.viewDidMoveToWindow()
            if let monitor {
                NSEvent.removeMonitor(monitor)
                self.monitor = nil
            }
            guard window != nil else { return }
            monitor = NSEvent.addLocalMonitorForEvents(matching: .rightMouseDown) { [weak self] event in
                guard let self, event.window === self.window else { return event }
                let point = self.convert(event.locationInWindow, from: nil)
                guard self.bounds.contains(point) else { return event }
                self.action()
                return nil
            }
        }

        deinit {
            if let monitor {
                NSEvent.removeMonitor(monitor)
            }
        }
    }
}

private extension Color {
    init(argb: UInt32) {
        self.init(
            .sRGB,
            red: Double((argb >> 16) & 255) / 255,
            green: Double((argb >> 8) & 255) / 255,
            blue: Double(argb & 255) / 255,
            opacity: Double((argb >> 24) & 255) / 255)
    }
}
