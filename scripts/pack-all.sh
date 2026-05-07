#!/usr/bin/env bash
# EN: Build per-TFM NuGet packages whose major version mirrors the target framework.
#     Library + CLI ship in lockstep. Output goes to ./out/.
# TR: Major sürümü hedef framework'ü yansıtan TFM-bazlı NuGet paketlerini üretir.
#     Library + CLI lockstep yayınlanır. Çıktı ./out/ klasörüne yazılır.
#
# Usage:
#   scripts/pack-all.sh                          # default minor.patch (.0.0)
#   scripts/pack-all.sh --minor 2 --patch 0      # produce 8.2.0 / 9.2.0 / 10.2.0
#   scripts/pack-all.sh --line net8 --minor 2 --patch 1   # only 8.2.1 (hotfix)

set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
OUT="$ROOT/out"
LIBRARY_PROJ="$ROOT/src/Configurite/Configurite.csproj"
CLI_PROJ="$ROOT/src/Configurite.Cli/Configurite.Cli.csproj"
ADMIN_PROJ="$ROOT/src/Configurite.AdminUI/Configurite.AdminUI.csproj"
POSTGRES_PROJ="$ROOT/src/Configurite.Postgres/Configurite.Postgres.csproj"

MINOR="0"
PATCH="0"
LINE="all"   # all | net8 | net9 | net10

while [[ $# -gt 0 ]]; do
    case "$1" in
        --minor) MINOR="$2"; shift 2 ;;
        --patch) PATCH="$2"; shift 2 ;;
        --line)  LINE="$2";  shift 2 ;;
        --help|-h)
            grep -E "^# " "$0" | head -10
            exit 0 ;;
        *) echo "unknown option: $1" >&2; exit 2 ;;
    esac
done

mkdir -p "$OUT"
rm -f "$OUT"/*.nupkg "$OUT"/*.snupkg

pack() {
    local tfm="$1"
    local major="$2"
    local version="$major.$MINOR.$PATCH"

    echo "==> Packing for $tfm @ $version"

    dotnet pack "$LIBRARY_PROJ" -c Release -o "$OUT" --nologo \
        -p:TargetFrameworks="$tfm" \
        -p:Version="$version" \
        -p:IncludeSymbols=true

    dotnet pack "$CLI_PROJ" -c Release -o "$OUT" --nologo \
        -p:TargetFrameworks="$tfm" \
        -p:Version="$version" \
        -p:IncludeSymbols=true

    dotnet pack "$ADMIN_PROJ" -c Release -o "$OUT" --nologo \
        -p:TargetFrameworks="$tfm" \
        -p:Version="$version" \
        -p:IncludeSymbols=true

    dotnet pack "$POSTGRES_PROJ" -c Release -o "$OUT" --nologo \
        -p:TargetFrameworks="$tfm" \
        -p:Version="$version" \
        -p:IncludeSymbols=true
}

case "$LINE" in
    all)
        pack "net8.0"  "8"
        pack "net9.0"  "9"
        pack "net10.0" "10"
        ;;
    net8)  pack "net8.0"  "8"  ;;
    net9)  pack "net9.0"  "9"  ;;
    net10) pack "net10.0" "10" ;;
    *) echo "unknown line: $LINE (use all|net8|net9|net10)" >&2; exit 2 ;;
esac

echo ""
echo "==> Output:"
ls -lh "$OUT"
