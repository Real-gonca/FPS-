#!/usr/bin/env bash
# Verificacao cruzada do bsboost sem Windows: compila DLL + CLI para
# x86_64-windows-gnu com zig cc. Pega erro de sintaxe, de link e warnings.
#
# Requer zig no PATH (ou `pip install ziglang` e aponte ZIG=...).
set -euo pipefail

cd "$(dirname "$0")"

ZIG="${ZIG:-$(command -v zig || true)}"
if [[ -z "${ZIG}" ]]; then
    ZIG="$(python3 - <<'PY' 2>/dev/null || true
import os, ziglang
print(os.path.join(os.path.dirname(ziglang.__file__), "zig"))
PY
)"
fi
if [[ -z "${ZIG}" || ! -x "${ZIG}" || -d "${ZIG}" ]]; then
    echo "zig nao encontrado. Instale com: pip install ziglang" >&2
    exit 1
fi

OUT="$(mktemp -d)"
trap 'rm -rf "${OUT}"' EXIT

CFLAGS=(-target x86_64-windows-gnu -O2 -Wall -Wextra -Werror -I.)

echo "== bsboost.dll =="
"${ZIG}" cc "${CFLAGS[@]}" -shared -o "${OUT}/bsboost.dll" bsboost.c bsboost.def

echo "== bsboostctl.exe =="
"${ZIG}" cc "${CFLAGS[@]}" -municode -o "${OUT}/bsboostctl.exe" \
    host/bsboostctl.c -L"${OUT}" -lbsboost

echo
echo "OK - binarios gerados em ${OUT}:"
ls -la "${OUT}"
echo
echo "Checagem de exports:"
for sym in BsBoostAttach BsBoostRestore BsBoostApplySelf BsBoostSetGpuPreference BsBoostFindProcess; do
    if grep -qa "${sym}" "${OUT}/bsboost.dll"; then
        echo "  [ok] ${sym}"
    else
        echo "  [FALTA] ${sym}" >&2
        exit 1
    fi
done
