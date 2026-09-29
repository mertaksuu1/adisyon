#!/usr/bin/env bash
# Restoran bilgisayarına kurulacak paketi üretir: web arayüzünü derler, API'nin içine koyar ve
# programı tek klasör hâlinde yayınlar.
#
# Kullanım:  scripts/build-package.sh [runtime]
#   runtime  varsayılan: win-x64 (Windows). Mac'te denemek için: osx-arm64
# Çıktı:     dist/adisyon-<runtime>/  (içinde Adisyon.Api(.exe), wwwroot/, appsettings*.json)
set -euo pipefail
cd "$(dirname "$0")/.."

RUNTIME="${1:-win-x64}"
OUT="dist/adisyon-$RUNTIME"

echo "1/3 Web arayüzü derleniyor…"
npm --prefix web ci --silent
npm --prefix web run build

echo "2/3 Web dosyaları API'nin wwwroot klasörüne kopyalanıyor…"
rm -rf src/Adisyon.Api/wwwroot
cp -R web/dist src/Adisyon.Api/wwwroot

echo "3/3 Program yayınlanıyor ($RUNTIME)…"
rm -rf "$OUT"
# --self-contained: restoran bilgisayarına ayrıca .NET kurmak gerekmez.
dotnet publish src/Adisyon.Api -c Release -r "$RUNTIME" --self-contained -o "$OUT" -p:DebugType=None -v quiet

echo "Tamam: $OUT"
