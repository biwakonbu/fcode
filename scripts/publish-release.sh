#!/bin/bash
set -e

# fcode リリース版パブリッシュスクリプト
# Single File実行ファイル生成・配布準備

echo "🚀 fcode リリース版パブリッシュ開始..."

# プロジェクトルートに移動
cd "$(dirname "$0")/.."

# 既存のpublish出力をクリーンアップ
echo "🧹 既存のビルド出力をクリーンアップ..."
if [ -d "src/bin/Release" ]; then
    rm -rf src/bin/Release
fi
if [ -d "src/obj/Release" ]; then
    rm -rf src/obj/Release
fi

# プラットフォーム別ビルド
echo "🔨 プラットフォーム別リリースビルド開始..."

# Linux x64 (推奨プラットフォーム)
echo "📦 Linux x64版ビルド中..."
dotnet publish src/fcode.fsproj \
    -c Release \
    -r linux-x64 \
    --self-contained true \
    -p:PublishSingleFile=true \
    -p:PublishTrimmed=true \
    -p:PublishReadyToRun=true \
    -p:IncludeNativeLibrariesForSelfExtract=true

# macOS x64
echo "📦 macOS x64版ビルド中..."
dotnet publish src/fcode.fsproj \
    -c Release \
    -r osx-x64 \
    --self-contained true \
    -p:PublishSingleFile=true \
    -p:PublishTrimmed=true \
    -p:PublishReadyToRun=true \
    -p:IncludeNativeLibrariesForSelfExtract=true

# macOS ARM64 (Apple Silicon)
echo "📦 macOS ARM64版ビルド中..."
dotnet publish src/fcode.fsproj \
    -c Release \
    -r osx-arm64 \
    --self-contained true \
    -p:PublishSingleFile=true \
    -p:PublishTrimmed=true \
    -p:PublishReadyToRun=true \
    -p:IncludeNativeLibrariesForSelfExtract=true

echo "✅ パブリッシュ完了！"
echo ""
echo "📁 生成されたバイナリ:"
echo "  Linux x64:    src/bin/Release/net8.0/linux-x64/publish/fcode"
echo "  macOS x64:    src/bin/Release/net8.0/osx-x64/publish/fcode"
echo "  macOS ARM64:  src/bin/Release/net8.0/osx-arm64/publish/fcode"
echo ""

# ファイルサイズ確認
echo "📊 バイナリサイズ:"
if [ -f "src/bin/Release/net8.0/linux-x64/publish/fcode" ]; then
    echo "  Linux x64:    $(du -h src/bin/Release/net8.0/linux-x64/publish/fcode | cut -f1)"
fi
if [ -f "src/bin/Release/net8.0/osx-x64/publish/fcode" ]; then
    echo "  macOS x64:    $(du -h src/bin/Release/net8.0/osx-x64/publish/fcode | cut -f1)"
fi
if [ -f "src/bin/Release/net8.0/osx-arm64/publish/fcode" ]; then
    echo "  macOS ARM64:  $(du -h src/bin/Release/net8.0/osx-arm64/publish/fcode | cut -f1)"
fi

echo ""
echo "🎯 クイックテスト:"
echo "  # Linux x64版テスト"
echo "  ./src/bin/Release/net8.0/linux-x64/publish/fcode --version"
echo ""
echo "📋 インストール手順:"
echo "  # システム全体インストール (Linux)"
echo "  sudo cp ./src/bin/Release/net8.0/linux-x64/publish/fcode /usr/local/bin/"
echo "  # または"
echo "  sudo install -m 755 ./src/bin/Release/net8.0/linux-x64/publish/fcode /usr/local/bin/"
echo ""
echo "  # macOS用 (Homebrewスタイル)"
echo "  cp ./src/bin/Release/net8.0/osx-*/publish/fcode /usr/local/bin/"
echo ""
echo "🚀 リリース準備完了！"