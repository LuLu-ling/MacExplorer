import AppKit
import SwiftUI

public typealias MXRenameText = @convention(c) (UnsafeMutableRawPointer?, UnsafePointer<CChar>?) -> Void
public typealias MXRenameDone = @convention(c) (UnsafeMutableRawPointer?) -> Void

@_cdecl("MXRenameCreate")
public func MXRenameCreate(
    _ context: UnsafeMutableRawPointer?,
    _ text: UnsafePointer<CChar>?,
    _ fontSize: Double,
    _ centered: Int32,
    _ changed: MXRenameText?,
    _ commit: MXRenameDone?,
    _ cancel: MXRenameDone?
) -> UnsafeMutableRawPointer {
    let model = RenameModel(
        context: context,
        text: text.map { String(cString: $0) } ?? "",
        fontSize: fontSize,
        centered: centered != 0,
        changed: changed,
        commit: commit,
        cancel: cancel)
    return Unmanaged.passRetained(RenameContainer(model: model)).toOpaque()
}

@_cdecl("MXRenameSilence")
public func MXRenameSilence(_ view: UnsafeMutableRawPointer) {
    Unmanaged<RenameContainer>.fromOpaque(view).takeUnretainedValue().silence()
}

@_cdecl("MXRenameRelease")
public func MXRenameRelease(_ view: UnsafeMutableRawPointer) {
    Unmanaged<RenameContainer>.fromOpaque(view).release()
}

private final class RenameModel {
    let context: UnsafeMutableRawPointer?
    let fontSize: CGFloat
    let centered: Bool
    let changed: MXRenameText?
    let commitCb: MXRenameDone?
    let cancelCb: MXRenameDone?
    var text: String
    private var closed = false

    init(
        context: UnsafeMutableRawPointer?,
        text: String,
        fontSize: Double,
        centered: Bool,
        changed: MXRenameText?,
        commit: MXRenameDone?,
        cancel: MXRenameDone?
    ) {
        self.context = context
        self.text = text
        self.fontSize = fontSize
        self.centered = centered
        self.changed = changed
        commitCb = commit
        cancelCb = cancel
    }

    func close() { closed = true }

    func push(_ value: String) {
        text = value
        guard !closed else { return }
        value.withCString { changed?(context, $0) }
    }

    func commit() {
        guard !closed else { return }
        closed = true
        commitCb?(context)
    }

    func cancel() {
        guard !closed else { return }
        closed = true
        cancelCb?(context)
    }
}

private struct RenameBox: View {
    let model: RenameModel

    var body: some View {
        Field(model: model)
            .padding(.horizontal, 4)
            .background(Color(nsColor: .textBackgroundColor), in: RoundedRectangle(cornerRadius: 5, style: .continuous))
            .overlay(
                RoundedRectangle(cornerRadius: 5, style: .continuous)
                    .strokeBorder(Color.accentColor, lineWidth: 1.5))
    }
}

private struct Field: NSViewRepresentable {
    let model: RenameModel

    func makeCoordinator() -> Coordinator { Coordinator(model) }

    func makeNSView(context: Context) -> Editor {
        let field = Editor(string: model.text)
        field.isBezeled = false
        field.isBordered = false
        field.drawsBackground = false
        field.focusRingType = .none
        field.font = .systemFont(ofSize: model.fontSize)
        field.alignment = model.centered ? .center : .left
        field.maximumNumberOfLines = 1
        field.delegate = context.coordinator
        if let cell = field.cell as? NSTextFieldCell {
            cell.wraps = false
            cell.isScrollable = true
            cell.usesSingleLineMode = true
        }
        return field
    }

    func updateNSView(_ field: Editor, context: Context) {
        context.coordinator.model = model
        if field.currentEditor() == nil && field.stringValue != model.text {
            field.stringValue = model.text
        }
        if field.font?.pointSize != model.fontSize {
            field.font = .systemFont(ofSize: model.fontSize)
        }
        let alignment: NSTextAlignment = model.centered ? .center : .left
        if field.alignment != alignment {
            field.alignment = alignment
        }
    }

    final class Coordinator: NSObject, NSTextFieldDelegate {
        var model: RenameModel
        private var editing = false
        private var live = false

        init(_ model: RenameModel) { self.model = model }

        func controlTextDidBeginEditing(_ notification: Notification) {
            editing = true
            if let editor = (notification.object as? NSTextField)?.currentEditor() as? NSTextView {
                Editor.configure(editor)
            }
            DispatchQueue.main.async { [weak self] in self?.live = true }
        }

        func controlTextDidChange(_ notification: Notification) {
            guard let field = notification.object as? NSTextField else { return }
            model.push(field.stringValue)
        }

        func control(_ control: NSControl, textView: NSTextView, doCommandBy commandSelector: Selector) -> Bool {
            if commandSelector == #selector(NSResponder.insertNewline(_:)) {
                model.push(textView.string)
                model.commit()
                return true
            }
            if commandSelector == #selector(NSResponder.cancelOperation(_:)) {
                model.cancel()
                return true
            }
            return false
        }

        func controlTextDidEndEditing(_ notification: Notification) {
            guard live, editing, (notification.object as? NSTextField)?.window != nil else { return }
            editing = false
            if let field = notification.object as? NSTextField {
                model.push(field.stringValue)
            }
            model.commit()
        }
    }
}

private final class Editor: NSTextField {
    private var armed = false

    override func viewDidMoveToWindow() {
        super.viewDidMoveToWindow()
        guard window != nil, !armed else { return }
        armed = true
        window?.makeFirstResponder(self)
        if !selectAll() {
            DispatchQueue.main.async { [weak self] in _ = self?.selectAll() }
        }
    }

    fileprivate static func configure(_ editor: NSTextView) {
        editor.isRichText = false
        editor.importsGraphics = false
        editor.isAutomaticQuoteSubstitutionEnabled = false
        editor.isAutomaticDashSubstitutionEnabled = false
        editor.isAutomaticTextReplacementEnabled = false
        editor.isAutomaticSpellingCorrectionEnabled = false
    }

    private func selectAll() -> Bool {
        guard let editor = currentEditor() as? NSTextView else { return false }
        Self.configure(editor)
        editor.selectAll(nil)
        return true
    }
}

private final class RenameHosting: NSHostingView<RenameBox> {
    override var isOpaque: Bool { false }

    required init(rootView: RenameBox) {
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

private final class RenameContainer: NSView {
    let model: RenameModel
    private let host: RenameHosting

    init(model: RenameModel) {
        self.model = model
        host = RenameHosting(rootView: RenameBox(model: model))
        super.init(frame: .zero)
        autoresizingMask = [.width, .height]
        host.frame = bounds
        addSubview(host)
    }

    required init?(coder: NSCoder) { nil }

    override var isOpaque: Bool { false }

    func silence() {
        model.close()
        window?.makeFirstResponder(nil)
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
