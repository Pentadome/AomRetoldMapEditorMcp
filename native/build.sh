#!/usr/bin/env bash
# Cross-build Windows x64 DLL on Linux; Windows remains required to run it.
set -euo pipefail
cd "$(dirname "$0")"
# C++17 supplies single-argument static_assert; _WIN32_WINNT 0x0A00 targets Windows 10 APIs.
# Static compiler runtimes avoid libgcc/libstdc++/winpthread DLL dependencies on user machines.
x86_64-w64-mingw32-g++ -std=c++17 -O2 -Wall -Wextra -Werror \
  -D_WIN32_WINNT=0x0A00 -shared -static -Wl,--no-insert-timestamp \
  EditorBridge.cpp -luser32 -lpsapi -o AomEditorBridge.dll
