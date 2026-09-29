#!/usr/bin/env bash
# Restoran bilgisayarına kurulacak paketi üretir: web arayüzünü derler, API'nin içine koyar, programı
# tek klasör hâlinde yayınlar ve (Windows için) kurulum betikleriyle birlikte ZIP'ler.
#
# Kullanım:  scripts/build-package.sh [runtime]
#   runtime  varsayılan: win-x64 (Windows). Mac'te denemek için: osx-arm64
# Çıktı:     dist/Adisyon-<runtime>/          program/ (+ Windows'ta Kur.cmd, Kaldir.cmd, BENIOKU.txt)
#            dist/Adisyon-<runtime>.zip        (yalnızca Windows paketi)
set -euo pipefail
cd "$(dirname "$0")/.."

RUNTIME="${1:-win-x64}"
OUT="dist/Adisyon-$RUNTIME"

echo "1/4 Web arayüzü derleniyor…"
npm --prefix web ci --silent
npm --prefix web run build

echo "2/4 Web dosyaları API'nin wwwroot klasörüne kopyalanıyor…"
rm -rf src/Adisyon.Api/wwwroot
cp -R web/dist src/Adisyon.Api/wwwroot

echo "3/4 Program yayınlanıyor ($RUNTIME)…"
rm -rf "$OUT" "$OUT.zip"
# --self-contained: restoran bilgisayarına ayrıca .NET kurmak gerekmez.
dotnet publish src/Adisyon.Api -c Release -r "$RUNTIME" --self-contained -o "$OUT/program" -p:DebugType=None -v quiet

if [[ "$RUNTIME" == win-* ]]; then
  echo "4/4 Kurulum dosyaları ekleniyor ve ZIP'leniyor…"
  cp installer/Kur.cmd installer/Kaldir.cmd installer/kur.ps1 installer/kaldir.ps1 installer/BENIOKU.txt "$OUT/"
  (cd dist && zip -qr "Adisyon-$RUNTIME.zip" "Adisyon-$RUNTIME")
  echo "Tamam: $OUT.zip ($(du -h "$OUT.zip" | cut -f1))"
else
  echo "4/4 (Windows dışı paket: kurulum dosyası yok)"
  echo "Tamam: $OUT/program"
fi
