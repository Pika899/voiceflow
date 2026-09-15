import AVFoundation
import ApplicationServices

public enum PermissionStatus {
    case granted
    case denied
    case notDetermined
}

public final class PermissionsManager {
    public init() {}

    public func microphoneStatus() -> PermissionStatus {
        switch AVCaptureDevice.authorizationStatus(for: .audio) {
        case .authorized: return .granted
        case .denied, .restricted: return .denied
        case .notDetermined: return .notDetermined
        @unknown default: return .denied
        }
    }

    public func requestMicrophoneAccess(completion: @escaping (Bool) -> Void) {
        AVCaptureDevice.requestAccess(for: .audio, completionHandler: completion)
    }

    public func accessibilityStatus() -> PermissionStatus {
        AXIsProcessTrusted() ? .granted : .denied
    }

    /// Shows the system's own "VoiceFlow wants to control this computer" prompt,
    /// which deep-links to System Settings > Privacy & Security > Accessibility.
    public func promptAccessibilityAccess() {
        let options = [kAXTrustedCheckOptionPrompt.takeUnretainedValue() as String: true] as CFDictionary
        _ = AXIsProcessTrustedWithOptions(options)
    }
}
