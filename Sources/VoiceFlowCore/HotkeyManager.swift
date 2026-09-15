import Carbon
import Foundation

public final class HotkeyManager {
    public var onPress: (() -> Void)?
    public var onRelease: (() -> Void)?

    private var hotKeyRef: EventHotKeyRef?
    private var eventHandlerRef: EventHandlerRef?
    private let hotKeyID: EventHotKeyID

    private static let signature: OSType = 0x56464C57 // 'VFLW'
    private nonisolated(unsafe) static var nextID: UInt32 = 1

    public init() {
        hotKeyID = EventHotKeyID(signature: Self.signature, id: Self.nextID)
        Self.nextID += 1
    }

    deinit {
        unregister()
    }

    /// Registers the global hotkey. Returns `false` if the system refused the
    /// registration — in practice `eventHotKeyExistsErr`, meaning another
    /// registration (any process, including this one) already owns the
    /// combination. The caller surfaces that to the user (Task 12).
    @discardableResult
    public func register(
        keyCode: UInt32 = UInt32(kVK_Space),
        modifiers: UInt32 = UInt32(controlKey | optionKey)
    ) -> Bool {
        guard hotKeyRef == nil else { return true }
        installEventHandlerIfNeeded()

        var ref: EventHotKeyRef?
        let status = RegisterEventHotKey(
            keyCode,
            modifiers,
            hotKeyID,
            GetApplicationEventTarget(),
            0,
            &ref
        )
        guard status == noErr, let ref else { return false }
        hotKeyRef = ref
        return true
    }

    public func unregister() {
        if let hotKeyRef {
            UnregisterEventHotKey(hotKeyRef)
            self.hotKeyRef = nil
        }
        if let eventHandlerRef {
            RemoveEventHandler(eventHandlerRef)
            self.eventHandlerRef = nil
        }
    }

    private func installEventHandlerIfNeeded() {
        guard eventHandlerRef == nil else { return }
        var eventTypes = [
            EventTypeSpec(eventClass: OSType(kEventClassKeyboard), eventKind: UInt32(kEventHotKeyPressed)),
            EventTypeSpec(eventClass: OSType(kEventClassKeyboard), eventKind: UInt32(kEventHotKeyReleased))
        ]
        let selfPointer = Unmanaged.passUnretained(self).toOpaque()
        InstallEventHandler(
            GetApplicationEventTarget(),
            hotkeyEventHandler,
            eventTypes.count,
            &eventTypes,
            selfPointer,
            &eventHandlerRef
        )
    }

    fileprivate func handle(event: EventRef?) -> OSStatus {
        var incomingID = EventHotKeyID()
        let status = GetEventParameter(
            event,
            EventParamName(kEventParamDirectObject),
            EventParamType(typeEventHotKeyID),
            nil,
            MemoryLayout<EventHotKeyID>.size,
            nil,
            &incomingID
        )
        guard status == noErr,
              incomingID.signature == hotKeyID.signature,
              incomingID.id == hotKeyID.id else {
            return OSStatus(eventNotHandledErr)
        }

        switch GetEventKind(event) {
        case UInt32(kEventHotKeyPressed):
            onPress?()
        case UInt32(kEventHotKeyReleased):
            onRelease?()
        default:
            return OSStatus(eventNotHandledErr)
        }
        return noErr
    }
}

// Carbon needs a C-convention function pointer; it hands back the manager
// through the userData pointer registered in installEventHandlerIfNeeded.
private func hotkeyEventHandler(
    _ callRef: EventHandlerCallRef?,
    _ event: EventRef?,
    _ userData: UnsafeMutableRawPointer?
) -> OSStatus {
    guard let userData else { return OSStatus(eventNotHandledErr) }
    let manager = Unmanaged<HotkeyManager>.fromOpaque(userData).takeUnretainedValue()
    return manager.handle(event: event)
}
