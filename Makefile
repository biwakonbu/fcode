# fcode プロジェクト用 Makefile
# 開発・CI/CD タスクの統一インターフェース

.PHONY: help setup clean build run test format lint check release install-tools hooks

# デフォルトターゲット
help:
	@echo "📋 fcode プロジェクト - 利用可能なコマンド:"
	@echo ""
	@echo "🔧 開発環境:"
	@echo "  setup          - プロジェクト初期セットアップ (Git hooks + ツール)"
	@echo "  install-tools  - 開発ツールのインストール (Fantomas等)"
	@echo "  hooks          - Git pre-commit フックの設定"
	@echo ""
	@echo "📝 コード品質:"
	@echo "  format         - F#コードの自動フォーマット (Fantomas)"
	@echo "  lint           - リント実行 (警告をエラーとして扱う)"
	@echo "  check          - 全品質チェック (フォーマット+リント+テスト)"
	@echo ""
	@echo "🏗️  ビルド・テスト:"
	@echo "  run            - アプリケーションを起動"
	@echo "  build          - デバッグビルド"
	@echo "  test           - テスト実行"
	@echo "  release        - リリースビルド + 単一ファイルパブリッシュ"
	@echo ""
	@echo "🧹 メンテナンス:"
	@echo "  clean          - ビルド成果物の削除"

# プロジェクト初期セットアップ
setup: install-tools hooks
	@echo "✅ プロジェクトセットアップ完了"

# 開発ツールのインストール
install-tools:
	@echo "🔧 開発ツールをインストール中..."
	@dotnet tool install -g fantomas || echo "Fantomas は既にインストール済み"
	@echo "✅ ツールインストール完了"

# Git hooks の設定
hooks:
	@echo "🪝 Git hooks を設定中..."
	@./.githooks/setup.sh

# F#コードの自動フォーマット
format:
	@echo "📝 F#コードをフォーマット中..."
	@fantomas src/ tests/
	@echo "✅ フォーマット完了"

# リント実行
lint:
	@echo "🔍 リント実行中..."
	@dotnet build src/fcode.fsproj --configuration Debug --verbosity normal --property TreatWarningsAsErrors=true
	@dotnet build tests/fcode.Tests.fsproj --configuration Debug --verbosity normal --property TreatWarningsAsErrors=true
	@echo "✅ リント完了"

# 全品質チェック (pre-commit相当)
check:
	@echo "🔍 全品質チェック実行中..."
	@if ./.githooks/pre-commit; then \
		echo "✅ 全品質チェック完了 - コミット可能です"; \
	else \
		echo ""; \
		echo "❌ 品質チェックに失敗しました"; \
		echo ""; \
		echo "📋 個別修正コマンド:"; \
		echo "  make format  # フォーマット修正"; \
		echo "  make lint    # リント警告修正"; \
		echo "  make test    # テスト失敗修正"; \
		echo "  make build   # ビルドエラー修正"; \
		echo ""; \
		echo "修正後に再度 make check を実行してください"; \
		exit 1; \
	fi

# アプリケーション起動
run:
	@echo "🚀 アプリケーションを起動中..."
	@dotnet run --project src/fcode.fsproj

# デバッグビルド
build:
	@echo "🏗️  デバッグビルド実行中..."
	@dotnet build src/fcode.fsproj --configuration Debug
	@dotnet build tests/fcode.Tests.fsproj --configuration Debug
	@echo "✅ ビルド完了"

# テスト実行
test:
	@echo "🧪 テスト実行中..."
	@dotnet test tests/fcode.Tests.fsproj --configuration Debug --verbosity normal
	@echo "✅ テスト完了"

# リリースビルド + パブリッシュ
release:
	@echo "🚀 リリースビルド実行中..."
	@dotnet build src/fcode.fsproj --configuration Release
	@dotnet build tests/fcode.Tests.fsproj --configuration Release
	@echo "📦 単一ファイルパブリッシュ実行中..."
	@dotnet publish src/fcode.fsproj -c Release -r linux-x64 --self-contained true -p:PublishSingleFile=true -o publish/linux-x64
	@dotnet publish src/fcode.fsproj -c Release -r osx-x64 --self-contained true -p:PublishSingleFile=true -o publish/osx-x64
	@echo "✅ リリース完了"
	@echo "📁 成果物:"
	@echo "  - publish/linux-x64/fcode"
	@echo "  - publish/osx-x64/fcode"

# ビルド成果物の削除
clean:
	@echo "🧹 ビルド成果物を削除中..."
	@dotnet clean src/fcode.fsproj
	@dotnet clean tests/fcode.Tests.fsproj
	@rm -rf publish/
	@echo "✅ クリーンアップ完了"