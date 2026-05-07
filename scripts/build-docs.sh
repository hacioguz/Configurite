#!/usr/bin/env bash
# EN: Builds the DocFX site. Mirrors docs/en/ + docs/tr/ into docs/site/, then runs docfx.
# TR: DocFX sitesini derler. docs/en/ + docs/tr/ içeriğini docs/site/'a yansıtır, sonra docfx çalıştırır.
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
SITE="$ROOT/docs/site"

# 1) Mirror markdown content (TOCs already live under docs/site/{en,tr}).
#    Markdown içeriğini yansıt (TOC'lar docs/site/{en,tr} altında).
for lang in en tr; do
    mkdir -p "$SITE/$lang"
    rsync -a --include='*.md' --exclude='*' "$ROOT/docs/$lang/" "$SITE/$lang/"
done

# 1b) Mirror brand assets — DocFX copies these to _site/ via the resource glob
#     in docfx.json ("icon.png", "logo-*.png"). _appLogoPath/_appFaviconPath
#     reference icon.png by name; the README hero image references logo-400.png.
#     Marka asset'lerini yansıt — docfx.json resource glob'u (icon.png, logo-*.png)
#     bunları _site/'a kopyalar.
cp "$ROOT/assets/icon.png"     "$SITE/icon.png"
cp "$ROOT/assets/logo-400.png" "$SITE/logo-400.png"
cp "$ROOT/assets/logo-800.png" "$SITE/logo-800.png"

# 2) Restore the pinned DocFX tool (.config/dotnet-tools.json is committed).
#    Sabitlenmiş DocFX aracını restore et (.config/dotnet-tools.json commit edilmiş).
cd "$ROOT"
dotnet tool restore >/dev/null

# 3) Build.
#    Derle.
echo "==> docfx build"
dotnet docfx "$SITE/docfx.json" --output "$SITE/_site"

echo ""
echo "==> Done. Open $SITE/_site/index.html"
