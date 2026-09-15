#!/bin/bash
set -euo pipefail
cd "$(dirname "$0")/.."

echo "Building VoiceFlowApp (release)..."
swift build -c release --product VoiceFlowApp

APP_DIR="dist/VoiceFlow.app"
rm -rf "$APP_DIR"
mkdir -p "$APP_DIR/Contents/MacOS"
mkdir -p "$APP_DIR/Contents/Resources"

cp .build/release/VoiceFlowApp "$APP_DIR/Contents/MacOS/VoiceFlow"
cp Resources/VoiceFlowApp/Info.plist "$APP_DIR/Contents/Info.plist"

# Prefer the local "VoiceFlow Dev" certificate (see scripts/make-signing-cert.sh):
# it gives the app a stable identity across rebuilds, so Accessibility and
# Microphone grants keep applying. Ad-hoc signing changes identity on every
# build and silently invalidates those grants.
IDENTITY="VoiceFlow Dev"
if security find-identity -v -p codesigning 2>/dev/null | grep -q "\"$IDENTITY\""; then
    echo "Signing with \"$IDENTITY\"..."
    codesign --force --sign "$IDENTITY" "$APP_DIR"
else
    echo "Ad-hoc signing (run scripts/make-signing-cert.sh once for a stable identity)..."
    codesign --force --sign - "$APP_DIR"
fi

echo "Built $APP_DIR"
