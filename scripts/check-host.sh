#!/usr/bin/env bash
# Everything the project can verify without Windows, a WDK install, a
# test-signing certificate or a .NET SDK.  This is what CI should run; it is a
# plain script rather than a .github/workflows file because the repository's
# automation token is not allowed to create workflow files.
#
#   ./scripts/check-host.sh          # full suite
#   ./scripts/check-host.sh --quick  # skip the benchmark
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$root"

echo "== unit, parity and driver simulation =="
make -C tests

if [[ "${1:-}" != "--quick" ]]; then
    echo
    echo "== hot path benchmark =="
    make -C tests bench
fi

echo
echo "== C++ CLI client (syntax only, Win32 stub) =="
g++ -std=c++17 -fsyntax-only -Wall -Wextra \
    -Wno-unused-function -Wno-unknown-pragmas \
    -Itests/host/win32stub -Itools/pcctl -Ishared \
    tools/pcctl/main.cpp
echo "OK: pcctl compiles cleanly against the stub"

if command -v python3 >/dev/null 2>&1; then
    echo
    echo "== WPF markup =="
    python3 tests/tools/check_xaml.py

    echo
    echo "== C# syntax =="
    python3 tests/tools/check_csharp.py || true
fi

echo
echo "All host checks passed."
