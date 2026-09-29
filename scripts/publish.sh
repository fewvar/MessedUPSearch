#!/bin/zsh
# Сборка релиза: Windows (.exe) и macOS (.app.zip) в dist/.
#   scripts/publish.sh 1.0
#
# Windows — win-x64, самодостаточный один файл: .NET, onnxruntime и Assets/Models внутри,
#   при запуске распаковываются во временную папку (поэтому AppContext.BaseDirectory видит Assets).
# macOS — osx-arm64, самодостаточный .app с Info.plist и иконкой, подпись ad-hoc
#   (без Apple Developer ID: при первом запуске — правый клик -> Открыть).
set -e

VERSION=${1:?версия, например 1.0}
ROOT=${0:A:h:h}
PROJECT="$ROOT/MessedUpSearchA/MessedUpSearchA.csproj"
DIST="$ROOT/dist"
rm -rf "$DIST" && mkdir -p "$DIST"

echo "== Windows"
dotnet publish "$PROJECT" -c Release -r win-x64 --self-contained true -v q -nologo \
  -p:PublishSingleFile=true -p:IncludeAllContentForSelfExtract=true -p:IncludeNativeLibrariesForSelfExtract=true \
  -p:DebugType=none -o "$DIST/win"
mv "$DIST/win/MessedUpSearchA.exe" "$DIST/MessedUpSearch-win-x64.exe"
rm -rf "$DIST/win"

echo "== macOS"
dotnet publish "$PROJECT" -c Release -r osx-arm64 --self-contained true -v q -nologo \
  -p:DebugType=none -o "$DIST/mac"
APP="$DIST/MessedUpSearch.app"
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"
cp -R "$DIST/mac/." "$APP/Contents/MacOS/"
rm -rf "$DIST/mac"
cp "$ROOT/MessedUpSearchA/Assets/Icons/app.icns" "$APP/Contents/Resources/"
cat > "$APP/Contents/Info.plist" <<PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
    <key>CFBundleName</key><string>MessedUpSearch</string>
    <key>CFBundleDisplayName</key><string>MessedUpSearch</string>
    <key>CFBundleIdentifier</key><string>com.fewvar.messedupsearch</string>
    <key>CFBundleVersion</key><string>$VERSION</string>
    <key>CFBundleShortVersionString</key><string>$VERSION</string>
    <key>CFBundlePackageType</key><string>APPL</string>
    <key>CFBundleExecutable</key><string>MessedUpSearchA</string>
    <key>CFBundleIconFile</key><string>app.icns</string>
    <key>NSHighResolutionCapable</key><true/>
    <key>LSMinimumSystemVersion</key><string>11.0</string>
    <key>NSRequiresAquaSystemAppearance</key><false/>
</dict>
</plist>
PLIST
codesign --force --deep --sign - "$APP"
(cd "$DIST" && ditto -c -k --keepParent MessedUpSearch.app MessedUpSearch.app.zip)

cp "$ROOT/ml/data/artists_index_v3.bin" "$DIST/"
ls -la "$DIST"
