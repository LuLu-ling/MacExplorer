import AppKit
import Combine
import SwiftUI

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
    view.model.context = context
    view.model.themeChanged = themeChanged
    view.model.languageChanged = languageChanged
    view.model.toggleChanged = toggleChanged
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
    var cardArgb: UInt32 = 0
    var strokeArgb: UInt32 = 0

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
        page = min(max(Int(data.page), 0), 3)
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
        cardArgb = data.cardArgb
        strokeArgb = data.strokeArgb
    }
}

private final class SettingsModel: ObservableObject {
    @Published private(set) var state = SettingsState()

    var context: UnsafeMutableRawPointer?
    var themeChanged: MXIntCallback?
    var languageChanged: MXIntCallback?
    var toggleChanged: MXToggleCallback?

    private var applying = false

    func apply(_ data: MXSettingsPayload) {
        let next = SettingsState(data)
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

    func setFlag(_ index: Int, _ on: Bool) {
        guard !applying, (0..<Metrics.folderCount).contains(index) else { return }
        let bit = 1 << index
        let next = on ? state.flags | bit : state.flags & ~bit
        guard next != state.flags else { return }
        state.flags = next
        toggleChanged?(context, Int32(index), on ? 1 : 0)
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
            card(state)
                .padding(.top, 12)
            if state.page == 3 {
                Text(state.description)
                    .font(.system(size: 12))
                    .foregroundStyle(Color.secondary)
                    .frame(maxWidth: .infinity, alignment: .leading)
                    .padding(.top, 10)
            }
            Spacer(minLength: 8)
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
                    ForEach(0..<Metrics.folderCount, id: \.self) { index in
                        toggle(state.folderLabels[index], flag(index), divider: index + 1 < Metrics.folderCount)
                    }
                case 3:
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

    private func picker(_ title: String, _ options: [String], _ selection: Binding<Int>) -> some View {
        SettingsRow(title: title) {
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
