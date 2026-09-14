#!/bin/bash
set -euo pipefail
cd "$(dirname "$0")/.."

WHISPER_TAG="v1.9.4"
SRC_DIR="Vendor/whisper.cpp-src"
OUT_DIR="Vendor/whisper"

if ! command -v cmake >/dev/null 2>&1; then
    echo "cmake is required to build whisper.cpp. Install it with: brew install cmake"
    exit 1
fi

if [ ! -d "$SRC_DIR" ]; then
    echo "Cloning whisper.cpp $WHISPER_TAG..."
    git clone --depth 1 --branch "$WHISPER_TAG" https://github.com/ggml-org/whisper.cpp "$SRC_DIR"
fi

echo "Building whisper.cpp (static, Metal embedded)..."
cmake -S "$SRC_DIR" -B "$SRC_DIR/build" \
    -DCMAKE_BUILD_TYPE=Release \
    -DBUILD_SHARED_LIBS=OFF \
    -DGGML_METAL=ON \
    -DGGML_METAL_EMBED_LIBRARY=ON \
    -DGGML_ACCELERATE=ON \
    -DWHISPER_BUILD_TESTS=OFF \
    -DWHISPER_BUILD_EXAMPLES=OFF \
    -DCMAKE_INSTALL_PREFIX="$(pwd)/$OUT_DIR"

cmake --build "$SRC_DIR/build" --config Release -j"$(sysctl -n hw.ncpu)"
cmake --install "$SRC_DIR/build"

echo "Static libraries installed in $OUT_DIR/lib:"
ls -1 "$OUT_DIR/lib"
echo "Headers installed in $OUT_DIR/include:"
ls -1 "$OUT_DIR/include"
