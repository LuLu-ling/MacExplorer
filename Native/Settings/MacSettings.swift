import AppKit

public typealias MXIntCallback = @convention(c) (UnsafeMutableRawPointer?, Int32) -> Void
public typealias MXToggleCallback = @convention(c) (UnsafeMutableRawPointer?, Int32, Int32) -> Void

@_cdecl("MXSettingsCreate")
public func MXSettingsCreate(
    _ context: UnsafeMutableRawPointer?,
    _ themeChanged: MXIntCallback?,
    _ languageChanged: MXIntCallback?,
    _ toggleChanged: MXToggleCallback?
) -> UnsafeMutableRawPointer {
    let view = SettingsRootView()
    view.context = context
    view.themeChanged = themeChanged
    view.languageChanged = languageChanged
    view.toggleChanged = toggleChanged
    return Unmanaged.passRetained(view).toOpaque()
}

@_cdecl("MXSettingsApply")
public func MXSettingsApply(
    _ view: UnsafeMutableRawPointer,
    _ page: Int32,
    _ theme: Int32,
    _ languageIndex: Int32,
    _ flags: Int32,
    _ appearanceTitle: UnsafePointer<CChar>?,
    _ languageTitle: UnsafePointer<CChar>?,
    _ foldersTitle: UnsafePointer<CChar>?,
    _ aboutTitle: UnsafePointer<CChar>?,
    _ themeLabel: UnsafePointer<CChar>?,
    _ themeSystem: UnsafePointer<CChar>?,
    _ themeLight: UnsafePointer<CChar>?,
    _ themeDark: UnsafePointer<CChar>?,
    _ languageLabel: UnsafePointer<CChar>?,
    _ languages: UnsafePointer<CChar>?,
    _ showHidden: UnsafePointer<CChar>?,
    _ showExtensions: UnsafePointer<CChar>?,
    _ showQuickAccess: UnsafePointer<CChar>?,
    _ showVolumes: UnsafePointer<CChar>?,
    _ showRecents: UnsafePointer<CChar>?,
    _ appName: UnsafePointer<CChar>?,
    _ version: UnsafePointer<CChar>?,
    _ description: UnsafePointer<CChar>?
) {
    let root = Unmanaged<SettingsRootView>.fromOpaque(view).takeUnretainedValue()
    root.apply(
        page: page,
        theme: theme,
        languageIndex: languageIndex,
        flags: flags,
        appearanceTitle: cString(appearanceTitle),
        languageTitle: cString(languageTitle),
        foldersTitle: cString(foldersTitle),
        aboutTitle: cString(aboutTitle),
        themeLabel: cString(themeLabel),
        themeTitles: [cString(themeSystem), cString(themeLight), cString(themeDark)],
        languageLabel: cString(languageLabel),
        languages: lines(languages),
        showHidden: cString(showHidden),
        showExtensions: cString(showExtensions),
        showQuickAccess: cString(showQuickAccess),
        showVolumes: cString(showVolumes),
        showRecents: cString(showRecents),
        appName: cString(appName),
        version: cString(version),
        description: cString(description)
    )
}

@_cdecl("MXSettingsRelease")
public func MXSettingsRelease(_ view: UnsafeMutableRawPointer) {
    Unmanaged<SettingsRootView>.fromOpaque(view).release()
}

private func cString(_ ptr: UnsafePointer<CChar>?) -> String {
    guard let ptr else { return "" }
    return String(cString: ptr)
}

private func lines(_ ptr: UnsafePointer<CChar>?) -> [String] {
    let raw = cString(ptr)
    return raw.isEmpty ? [] : raw.split(separator: "\n", omittingEmptySubsequences: false).map(String.init)
}

private final class SettingsRootView: NSView {
    var context: UnsafeMutableRawPointer?
    var themeChanged: MXIntCallback?
    var languageChanged: MXIntCallback?
    var toggleChanged: MXToggleCallback?

    private var applying = false
    private var layingOut = false

    private let scroll = NSScrollView()
    private let document = FlippedView()
    private let appearancePage = Column()
    private let languagePage = Column()
    private let foldersPage = Column()
    private let aboutPage = Column()

    private let appearanceTitle = makeLabel(size: 22, weight: .semibold)
    private let themeCaption = makeLabel(size: 13, color: .secondaryLabelColor)
    private let themePopup = NSPopUpButton(frame: .zero, pullsDown: false)
    private let languageTitle = makeLabel(size: 22, weight: .semibold)
    private let languageCaption = makeLabel(size: 13, color: .secondaryLabelColor)
    private let languagePopup = NSPopUpButton(frame: .zero, pullsDown: false)
    private let foldersTitle = makeLabel(size: 22, weight: .semibold)
    private let hiddenRow = ToggleRow(id: 0)
    private let extensionsRow = ToggleRow(id: 1)
    private let quickAccessRow = ToggleRow(id: 2)
    private let volumesRow = ToggleRow(id: 3)
    private let recentsRow = ToggleRow(id: 4)
    private let aboutTitle = makeLabel(size: 22, weight: .semibold)
    private let appName = makeLabel(size: 18, weight: .medium)
    private let version = makeLabel(size: 13, color: .secondaryLabelColor)
    private let aboutDescription = makeWrappingLabel()

    override var isFlipped: Bool { true }
    override var isOpaque: Bool { false }

    override init(frame frameRect: NSRect) {
        super.init(frame: frameRect)
        wantsLayer = true
        layer?.backgroundColor = NSColor.clear.cgColor
        autoresizingMask = [.width, .height]
        autoresizesSubviews = true

        let clip = FlippedClipView()
        clip.drawsBackground = false
        scroll.contentView = clip
        scroll.documentView = document
        scroll.drawsBackground = false
        scroll.borderType = .noBorder
        scroll.hasVerticalScroller = true
        scroll.hasHorizontalScroller = false
        scroll.autohidesScrollers = true
        scroll.horizontalScrollElasticity = .none
        scroll.autoresizingMask = [.width, .height]
        addSubview(scroll)

        document.addSubview(appearancePage)
        document.addSubview(languagePage)
        document.addSubview(foldersPage)
        document.addSubview(aboutPage)

        appearancePage.add(appearanceTitle, spaceBefore: 0)
        appearancePage.add(themeCaption, spaceBefore: 16)
        appearancePage.add(themePopup, fill: false, spaceBefore: 8)
        languagePage.add(languageTitle, spaceBefore: 0)
        languagePage.add(languageCaption, spaceBefore: 16)
        languagePage.add(languagePopup, fill: false, spaceBefore: 8)
        foldersPage.add(foldersTitle, spaceBefore: 0)
        foldersPage.add(hiddenRow, spaceBefore: 16)
        foldersPage.add(extensionsRow, spaceBefore: 12)
        foldersPage.add(quickAccessRow, spaceBefore: 12)
        foldersPage.add(volumesRow, spaceBefore: 12)
        foldersPage.add(recentsRow, spaceBefore: 12)
        aboutPage.add(aboutTitle, spaceBefore: 0)
        aboutPage.add(appName, spaceBefore: 16)
        aboutPage.add(version, spaceBefore: 4)
        aboutPage.add(aboutDescription, spaceBefore: 12)

        configure(themePopup, action: #selector(onTheme))
        configure(languagePopup, action: #selector(onLanguage))
        for row in [hiddenRow, extensionsRow, quickAccessRow, volumesRow, recentsRow] {
            row.toggle.target = self
            row.toggle.action = #selector(onToggle(_:))
        }
    }

    required init?(coder: NSCoder) { nil }

    override func setFrameSize(_ newSize: NSSize) {
        super.setFrameSize(newSize)
        layoutContent()
    }

    override func viewDidMoveToWindow() {
        super.viewDidMoveToWindow()
        layoutContent()
    }

    override func layout() {
        super.layout()
        layoutContent()
    }

    func apply(
        page: Int32,
        theme: Int32,
        languageIndex: Int32,
        flags: Int32,
        appearanceTitle: String,
        languageTitle: String,
        foldersTitle: String,
        aboutTitle: String,
        themeLabel: String,
        themeTitles: [String],
        languageLabel: String,
        languages: [String],
        showHidden: String,
        showExtensions: String,
        showQuickAccess: String,
        showVolumes: String,
        showRecents: String,
        appName: String,
        version: String,
        description: String
    ) {
        applying = true
        defer { applying = false }

        self.appearanceTitle.stringValue = appearanceTitle
        self.languageTitle.stringValue = languageTitle
        self.foldersTitle.stringValue = foldersTitle
        self.aboutTitle.stringValue = aboutTitle
        themeCaption.stringValue = themeLabel
        languageCaption.stringValue = languageLabel
        hiddenRow.label.stringValue = showHidden
        extensionsRow.label.stringValue = showExtensions
        quickAccessRow.label.stringValue = showQuickAccess
        volumesRow.label.stringValue = showVolumes
        recentsRow.label.stringValue = showRecents
        self.appName.stringValue = appName
        self.version.stringValue = version
        aboutDescription.stringValue = description

        refill(themePopup, titles: themeTitles, selected: Int(theme))
        refill(languagePopup, titles: languages, selected: Int(languageIndex))
        hiddenRow.toggle.state = bit(flags, 0) ? .on : .off
        extensionsRow.toggle.state = bit(flags, 1) ? .on : .off
        quickAccessRow.toggle.state = bit(flags, 2) ? .on : .off
        volumesRow.toggle.state = bit(flags, 3) ? .on : .off
        recentsRow.toggle.state = bit(flags, 4) ? .on : .off

        themePopup.setAccessibilityLabel(themeLabel)
        languagePopup.setAccessibilityLabel(languageLabel)

        appearancePage.isHidden = page != 0
        languagePage.isHidden = page != 1
        foldersPage.isHidden = page != 2
        aboutPage.isHidden = page != 3
        needsLayout = true
        layoutContent()
    }

    private func configure(_ popup: NSPopUpButton, action: Selector) {
        popup.controlSize = .regular
        popup.autoenablesItems = false
        popup.target = self
        popup.action = action
    }

    private func refill(_ popup: NSPopUpButton, titles: [String], selected: Int) {
        if popup.itemTitles != titles {
            popup.removeAllItems()
            popup.addItems(withTitles: titles)
        }
        if titles.isEmpty {
            popup.selectItem(at: -1)
            return
        }
        popup.selectItem(at: min(max(selected, 0), titles.count - 1))
    }

    private func layoutContent() {
        guard !layingOut else { return }
        layingOut = true
        defer { layingOut = false }

        scroll.frame = bounds
        let width = max(bounds.width, 1)
        let inner = min(640, max(width - 64, 1))
        var y: CGFloat = 32
        for page in [appearancePage, languagePage, foldersPage, aboutPage] where !page.isHidden {
            let height = page.measure(width: inner)
            page.frame = NSRect(x: 32, y: y, width: inner, height: height)
            y += height
        }
        y += 32
        document.frame = NSRect(x: 0, y: 0, width: width, height: max(y, 1))
    }

    @objc private func onTheme() {
        guard !applying else { return }
        themeChanged?(context, Int32(themePopup.indexOfSelectedItem))
    }

    @objc private func onLanguage() {
        guard !applying else { return }
        languageChanged?(context, Int32(languagePopup.indexOfSelectedItem))
    }

    @objc private func onToggle(_ sender: NSSwitch) {
        guard !applying else { return }
        toggleChanged?(context, Int32(sender.tag), sender.state == .on ? 1 : 0)
    }
}

private final class FlippedClipView: NSClipView {
    override var isFlipped: Bool { true }
    override var isOpaque: Bool { false }
}

private final class FlippedView: NSView {
    override var isFlipped: Bool { true }
    override var isOpaque: Bool { false }
}

private final class Column: NSView {
    override var isFlipped: Bool { true }
    override var isOpaque: Bool { false }

    private var items: [(view: NSView, fill: Bool, spaceBefore: CGFloat)] = []

    func add(_ view: NSView, fill: Bool = true, spaceBefore: CGFloat) {
        items.append((view, fill, spaceBefore))
        addSubview(view)
    }

    func measure(width: CGFloat) -> CGFloat {
        var y: CGFloat = 0
        for item in items {
            y += item.spaceBefore
            let height: CGFloat
            let rowWidth: CGFloat
            if let field = item.view as? NSTextField, !field.usesSingleLineMode {
                field.preferredMaxLayoutWidth = width
                height = max(field.intrinsicContentSize.height, 18)
                rowWidth = width
            } else if let popup = item.view as? NSPopUpButton {
                popup.sizeToFit()
                height = max(popup.fittingSize.height, 21)
                rowWidth = min(240, width)
            } else if item.view is ToggleRow {
                height = 24
                rowWidth = width
            } else if let control = item.view as? NSControl {
                control.sizeToFit()
                height = max(control.fittingSize.height, 18)
                rowWidth = item.fill ? width : min(control.fittingSize.width, width)
            } else {
                height = max(item.view.fittingSize.height, 18)
                rowWidth = item.fill ? width : min(item.view.fittingSize.width, width)
            }
            item.view.frame = NSRect(x: 0, y: y, width: max(rowWidth, 1), height: height)
            y += height
        }
        return max(y, 1)
    }
}

private final class ToggleRow: NSView {
    let label = makeLabel(size: 13)
    let toggle = NSSwitch()

    override var isFlipped: Bool { true }
    override var isOpaque: Bool { false }

    init(id: Int) {
        super.init(frame: .zero)
        toggle.tag = id
        toggle.controlSize = .regular
        addSubview(label)
        addSubview(toggle)
    }

    required init?(coder: NSCoder) { nil }

    override func layout() {
        super.layout()
        toggle.sizeToFit()
        let size = toggle.fittingSize
        toggle.frame = NSRect(
            x: bounds.width - size.width,
            y: (bounds.height - size.height) / 2,
            width: size.width,
            height: size.height)
        label.frame = NSRect(
            x: 0,
            y: 0,
            width: max(bounds.width - size.width - 10, 1),
            height: bounds.height)
    }
}

private func makeLabel(size: CGFloat, weight: NSFont.Weight = .regular, color: NSColor = .labelColor) -> NSTextField {
    let field = NSTextField(labelWithString: "")
    field.font = NSFont.systemFont(ofSize: size, weight: weight)
    field.textColor = color
    field.lineBreakMode = .byTruncatingTail
    field.maximumNumberOfLines = 1
    field.usesSingleLineMode = true
    field.setContentCompressionResistancePriority(.defaultLow, for: .horizontal)
    return field
}

private func makeWrappingLabel() -> NSTextField {
    let field = NSTextField(wrappingLabelWithString: "")
    field.font = NSFont.systemFont(ofSize: 13)
    field.textColor = .labelColor
    field.maximumNumberOfLines = 0
    field.usesSingleLineMode = false
    field.lineBreakMode = .byWordWrapping
    field.setContentCompressionResistancePriority(.defaultLow, for: .horizontal)
    return field
}

private func bit(_ flags: Int32, _ index: Int32) -> Bool {
    (flags & (1 << index)) != 0
}
