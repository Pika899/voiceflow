#!/bin/bash
# Runs the Swift Testing suite reliably on a Command Line Tools-only Mac.
#
# This CLT install ships the Swift Testing macro plugin under
# usr/lib/swift/host/plugins/testing/, a subdirectory the compiler only
# sometimes scans — plain `swift test` fails intermittently with
# "plugin for module 'TestingMacros' not found". Passing the plugin path
# explicitly makes every run deterministic. Any extra arguments are
# forwarded to `swift test` (e.g. `./scripts/test.sh --filter FooTests`).
set -euo pipefail
cd "$(dirname "$0")/.."

TOOLCHAIN="$(xcrun --find swift | sed 's|/bin/swift$||')"
PLUGIN_DIR="$TOOLCHAIN/lib/swift/host/plugins/testing"

if [ ! -d "$PLUGIN_DIR" ]; then
    echo "Swift Testing macro plugin directory not found at $PLUGIN_DIR" >&2
    exit 1
fi

exec swift test -Xswiftc -plugin-path -Xswiftc "$PLUGIN_DIR" "$@"
