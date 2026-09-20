import AppKit

public typealias MXIntCallback = @convention(c) (UnsafeMutableRawPointer?, Int32) -> Void
public typealias MXToggleCallback = @convention(c) (UnsafeMutableRawPointer?, Int32, Int32) -> Void

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
}

@_cdecl("MXSettingsCreate")
public func MXSettingsCreate(
    _ context: UnsafeMutableRawPointer?,
    _ themeChanged: MXIntCallback?,
    _ languageChanged: MXIntCallback?,
    _ toggleChanged: MXToggleCallback?
) -> UnsafeMutableRawPointer {
    let view = SettingsView(frame: .zero)
    view.context = context
    view.themeChanged = themeChanged
    view.languageChanged = languageChanged
    view.toggleChanged = toggleChanged
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
    static let rowHeight: CGFloat = 40
    static let rowInset: CGFloat = 14
}

private final class SettingsView: NSView {
    var context: UnsafeMutableRawPointer?
    var themeChanged: MXIntCallback?
    var languageChanged: MXIntCallback?
    var toggleChanged: MXToggleCallback?

    private var applying = false
    private var page = -1
    private var cardArgb: UInt32 = 0
    private var strokeArgb: UInt32 = 0
    private var body: NSView?
    private var bodyPins: [NSLayoutConstraint] = []
    private var footerPins: [NSLayoutConstraint] = []

    private let heading = makeLabel(size: 26, weight: .bold)
    private let card = makeCard()
    private let footer = makeLabel(size: 12, color: .secondaryLabelColor, wrapping: true)
    private let theme = PopupRow()
    private let language = PopupRow()
    private let flags: [ToggleRow]
    private let version = makeLabel(color: .secondaryLabelColor)
    private let about: Row
    private let folders: NSView

    override var isOpaque: Bool { false }

    override init(frame frameRect: NSRect) {
        let flags = (0..<5).map { ToggleRow(id: $0, divider: $0 < 4) }
        self.flags = flags
        folders = column(flags)
        about = Row(accessory: version)
        super.init(frame: frameRect)
        wantsLayer = true
        layer?.backgroundColor = NSColor.clear.cgColor
        autoresizingMask = [.width, .height]
        footer.isHidden = true
        version.setContentCompressionResistancePriority(.required, for: .horizontal)
        addSubview(heading)
        addSubview(card)
        addSubview(footer)
        footerPins = [
            footer.topAnchor.constraint(equalTo: card.bottomAnchor, constant: 10),
            footer.leadingAnchor.constraint(equalTo: heading.leadingAnchor),
            footer.trailingAnchor.constraint(equalTo: heading.trailingAnchor),
            footer.bottomAnchor.constraint(lessThanOrEqualTo: bottomAnchor, constant: -8)
        ]
        NSLayoutConstraint.activate([
            heading.topAnchor.constraint(equalTo: topAnchor, constant: 10),
            heading.leadingAnchor.constraint(equalTo: leadingAnchor),
            heading.trailingAnchor.constraint(equalTo: trailingAnchor),
            card.topAnchor.constraint(equalTo: heading.bottomAnchor, constant: 12),
            card.leadingAnchor.constraint(equalTo: heading.leadingAnchor),
            card.trailingAnchor.constraint(equalTo: heading.trailingAnchor),
            card.bottomAnchor.constraint(lessThanOrEqualTo: bottomAnchor, constant: -8)
        ])
        show(body: theme, page: 0)
        theme.popup.target = self
        theme.popup.action = #selector(onTheme)
        language.popup.target = self
        language.popup.action = #selector(onLanguage)
        for row in flags {
            row.toggle.target = self
            row.toggle.action = #selector(onToggle(_:))
        }
    }

    required init?(coder: NSCoder) { nil }

    func apply(_ data: MXSettingsPayload) {
        applying = true
        defer { applying = false }

        let titles = lines(data.pageTitles)
        let folderLabels = lines(data.folderLabels)
        let nextPage = min(max(Int(data.page), 0), 3)
        setText(heading, at: nextPage, in: titles)
        show(body: [theme, language, folders, about][nextPage], page: nextPage)
        setText(footer, data.description)
        setText(theme.label, data.themeLabel)
        setText(language.label, data.languageLabel)
        theme.refill(titles: lines(data.themeOptions), selected: Int(data.theme), label: theme.label.stringValue)
        language.refill(titles: lines(data.languages), selected: Int(data.languageIndex), label: language.label.stringValue)
        for (index, row) in flags.enumerated() {
            setText(row.label, at: index, in: folderLabels)
            let on: NSControl.StateValue = (data.flags & (1 << index)) != 0 ? .on : .off
            if row.toggle.state != on { row.toggle.state = on }
        }
        setText(about.label, data.appName)
        setText(version, data.version)
        paint(cardArgb: data.cardArgb, strokeArgb: data.strokeArgb)
        layoutSubtreeIfNeeded()
        needsDisplay = true
    }

    private func show(body next: NSView, page: Int) {
        let showFooter = page == 3
        if footer.isHidden == showFooter {
            footer.isHidden = !showFooter
            if showFooter {
                NSLayoutConstraint.activate(footerPins)
            } else {
                NSLayoutConstraint.deactivate(footerPins)
            }
        }
        guard page != self.page else { return }
        self.page = page
        NSLayoutConstraint.deactivate(bodyPins)
        body?.removeFromSuperview()
        next.translatesAutoresizingMaskIntoConstraints = false
        card.addSubview(next)
        bodyPins = [
            next.topAnchor.constraint(equalTo: card.topAnchor),
            next.leadingAnchor.constraint(equalTo: card.leadingAnchor),
            next.trailingAnchor.constraint(equalTo: card.trailingAnchor),
            next.bottomAnchor.constraint(equalTo: card.bottomAnchor)
        ]
        NSLayoutConstraint.activate(bodyPins)
        body = next
    }

    private func paint(cardArgb: UInt32, strokeArgb: UInt32) {
        self.cardArgb = cardArgb
        self.strokeArgb = strokeArgb
        applyCardColors()
    }

    private func applyCardColors() {
        card.layer?.backgroundColor = argbColor(cardArgb).cgColor
        card.layer?.borderColor = argbColor(strokeArgb).cgColor
    }

    override func viewDidChangeEffectiveAppearance() {
        super.viewDidChangeEffectiveAppearance()
        applyCardColors()
        needsDisplay = true
    }

    override func setFrameSize(_ newSize: NSSize) {
        super.setFrameSize(newSize)
        layoutSubtreeIfNeeded()
    }

    override func viewDidMoveToWindow() {
        super.viewDidMoveToWindow()
        layoutSubtreeIfNeeded()
    }

    @objc private func onTheme() {
        guard !applying else { return }
        themeChanged?(context, Int32(theme.popup.indexOfSelectedItem))
    }

    @objc private func onLanguage() {
        guard !applying else { return }
        languageChanged?(context, Int32(language.popup.indexOfSelectedItem))
    }

    @objc private func onToggle(_ sender: NSSwitch) {
        guard !applying else { return }
        toggleChanged?(context, Int32(sender.tag), sender.state == .on ? 1 : 0)
    }
}

private class Row: NSView {
    let label = makeLabel()

    init(accessory: NSView, divider: Bool = false) {
        super.init(frame: .zero)
        translatesAutoresizingMaskIntoConstraints = false
        accessory.translatesAutoresizingMaskIntoConstraints = false
        addSubview(label)
        addSubview(accessory)
        var constraints = [
            heightAnchor.constraint(equalToConstant: Metrics.rowHeight),
            label.leadingAnchor.constraint(equalTo: leadingAnchor, constant: Metrics.rowInset),
            label.centerYAnchor.constraint(equalTo: centerYAnchor),
            label.trailingAnchor.constraint(lessThanOrEqualTo: accessory.leadingAnchor, constant: -12),
            accessory.trailingAnchor.constraint(equalTo: trailingAnchor, constant: -Metrics.rowInset),
            accessory.centerYAnchor.constraint(equalTo: centerYAnchor)
        ]
        if divider {
            let line = NSBox()
            line.boxType = .separator
            line.translatesAutoresizingMaskIntoConstraints = false
            addSubview(line)
            constraints.append(contentsOf: [
                line.leadingAnchor.constraint(equalTo: leadingAnchor, constant: Metrics.rowInset),
                line.trailingAnchor.constraint(equalTo: trailingAnchor),
                line.bottomAnchor.constraint(equalTo: bottomAnchor)
            ])
        }
        NSLayoutConstraint.activate(constraints)
    }

    required init?(coder: NSCoder) { nil }
}

private final class PopupRow: Row {
    let popup: NSPopUpButton
    private let widthConstraint: NSLayoutConstraint
    private let heightConstraint: NSLayoutConstraint

    init() {
        let popup = NSPopUpButton(frame: .zero, pullsDown: false)
        popup.translatesAutoresizingMaskIntoConstraints = false
        popup.controlSize = .regular
        popup.autoenablesItems = false
        popup.bezelStyle = .rounded
        popup.isBordered = true
        popup.font = .systemFont(ofSize: NSFont.systemFontSize)
        self.popup = popup
        widthConstraint = popup.widthAnchor.constraint(equalToConstant: 1)
        heightConstraint = popup.heightAnchor.constraint(equalToConstant: 21)
        super.init(accessory: popup)
        NSLayoutConstraint.activate([widthConstraint, heightConstraint])
    }

    required init?(coder: NSCoder) { nil }

    func refill(titles: [String], selected: Int, label: String) {
        if popup.itemTitles != titles {
            popup.removeAllItems()
            popup.addItems(withTitles: titles)
            let probe = NSPopUpButton(frame: .zero, pullsDown: false)
            probe.controlSize = popup.controlSize
            probe.bezelStyle = popup.bezelStyle
            probe.isBordered = true
            probe.font = popup.font
            probe.addItems(withTitles: titles)
            probe.sizeToFit()
            widthConstraint.constant = max(ceil(probe.fittingSize.width), 1)
            heightConstraint.constant = max(ceil(probe.fittingSize.height), 21)
        }
        popup.setAccessibilityLabel(label)
        if titles.isEmpty {
            popup.selectItem(at: -1)
        } else {
            popup.selectItem(at: min(max(selected, 0), titles.count - 1))
        }
    }
}

private final class ToggleRow: Row {
    let toggle: NSSwitch

    init(id: Int, divider: Bool) {
        let toggle = NSSwitch()
        toggle.tag = id
        toggle.controlSize = .regular
        self.toggle = toggle
        super.init(accessory: toggle, divider: divider)
    }

    required init?(coder: NSCoder) { nil }
}

private func makeCard() -> NSView {
    let view = NSView()
    view.translatesAutoresizingMaskIntoConstraints = false
    view.wantsLayer = true
    view.layer?.backgroundColor = NSColor.clear.cgColor
    view.layer?.cornerRadius = 12
    view.layer?.cornerCurve = .continuous
    view.layer?.borderWidth = 1
    view.layer?.masksToBounds = true
    return view
}

private func makeLabel(
    size: CGFloat = 13,
    weight: NSFont.Weight = .regular,
    color: NSColor = .labelColor,
    wrapping: Bool = false
) -> NSTextField {
    let field = wrapping ? NSTextField(wrappingLabelWithString: "") : NSTextField(labelWithString: "")
    field.translatesAutoresizingMaskIntoConstraints = false
    field.font = .systemFont(ofSize: size, weight: weight)
    field.textColor = color
    field.maximumNumberOfLines = wrapping ? 0 : 1
    field.lineBreakMode = wrapping ? .byWordWrapping : .byTruncatingTail
    field.usesSingleLineMode = !wrapping
    field.setContentCompressionResistancePriority(.defaultLow, for: .horizontal)
    return field
}

private func column(_ rows: [NSView]) -> NSView {
    let view = NSView()
    view.translatesAutoresizingMaskIntoConstraints = false
    var top = view.topAnchor
    for row in rows {
        view.addSubview(row)
        NSLayoutConstraint.activate([
            row.topAnchor.constraint(equalTo: top),
            row.leadingAnchor.constraint(equalTo: view.leadingAnchor),
            row.trailingAnchor.constraint(equalTo: view.trailingAnchor)
        ])
        top = row.bottomAnchor
    }
    rows.last?.bottomAnchor.constraint(equalTo: view.bottomAnchor).isActive = true
    return view
}

private func setText(_ field: NSTextField, _ value: UnsafePointer<CChar>?) {
    let text = cString(value)
    if field.stringValue != text { field.stringValue = text }
}

private func setText(_ field: NSTextField, at index: Int, in items: [String]) {
    let text = items.indices.contains(index) ? items[index] : ""
    if field.stringValue != text { field.stringValue = text }
}

private func cString(_ ptr: UnsafePointer<CChar>?) -> String {
    guard let ptr else { return "" }
    return String(cString: ptr)
}

private func lines(_ ptr: UnsafePointer<CChar>?) -> [String] {
    let raw = cString(ptr)
    return raw.isEmpty ? [] : raw.split(separator: "\n", omittingEmptySubsequences: false).map(String.init)
}

private func argbColor(_ value: UInt32) -> NSColor {
    NSColor(
        srgbRed: CGFloat((value >> 16) & 0xFF) / 255,
        green: CGFloat((value >> 8) & 0xFF) / 255,
        blue: CGFloat(value & 0xFF) / 255,
        alpha: CGFloat((value >> 24) & 0xFF) / 255
    )
}
