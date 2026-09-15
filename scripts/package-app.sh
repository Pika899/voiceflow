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

echo "Ad-hoc signing (no Developer ID needed for local testing)..."
codesign --force --deep --sign - "$APP_DIR"

echo "Built $APP_DIR"
