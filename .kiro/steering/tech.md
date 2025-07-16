# 技術スタック・ビルドシステム

## 技術スタック

### 言語・フレームワーク
- **言語**: F# (.NET 8) - 関数型・型安全性・非同期処理・パターンマッチ
- **UIフレームワーク**: Terminal.Gui 1.15.0 - クロスプラットフォーム・安定性
- **対象プラットフォーム**: Linux, macOS (Windows対象外)

### 主要ライブラリ
- **プロセス管理**: .NET Process + Unix Domain Sockets - 高信頼性IPC
- **設定管理**: System.Text.Json - .NET標準・高性能シリアライゼーション
- **データベース**: Microsoft.Data.Sqlite - SQLite3統合・ACID保証
- **PTY統合**: Pty.Net 0.1.16-pre - ターミナル制御
- **テストフレームワーク**: NUnit 3.13.3 - 包括的テストスイート

### 開発ツール
- **コードフォーマット**: Fantomas - F#標準フォーマッター
- **リント**: FSharpLint - コード品質チェック
- **CI/CD**: GitHub Actions - Linux/macOS自動テスト

## ビルドシステム

### Makefileコマンド

#### 開発環境セットアップ
```bash
make setup          # プロジェクト初期セットアップ (Git hooks + ツール)
make install-tools  # 開発ツールのインストール (Fantomas等)
make hooks          # Git pre-commit フックの設定
```

#### コード品質管理
```bash
make format         # F#コードの自動フォーマット (Fantomas)
make lint           # リント実行 (警告をエラーとして扱う)
make check          # 全品質チェック (フォーマット+リント+テスト)
```

#### ビルド・テスト・実行
```bash
make run            # アプリケーションを起動
make build          # デバッグビルド
make test           # テスト実行 (82テストケース)
make release        # リリースビルド + 単一ファイルパブリッシュ
make clean          # ビルド成果物の削除
```

### .NETコマンド

#### 直接実行
```bash
# アプリケーション起動
dotnet run --project src/fcode.fsproj

# テスト実行
dotnet test tests/fcode.Tests.fsproj --configuration Debug --verbosity normal

# リリースビルド
dotnet build src/fcode.fsproj --configuration Release
```

#### パブリッシュ
```bash
# Linux x64向け単一ファイルパブリッシュ
dotnet publish src/fcode.fsproj -c Release -r linux-x64 --self-contained true -p:PublishSingleFile=true -o publish/linux-x64

# macOS x64向け単一ファイルパブリッシュ
dotnet publish src/fcode.fsproj -c Release -r osx-x64 --self-contained true -p:PublishSingleFile=true -o publish/osx-x64
```

## 品質保証

### 自動品質チェック
- **Pre-commitフック**: コミット時に自動実行
  - Fantomas: F#コードフォーマット自動チェック・修正
  - FSharpLint: コード品質・規約チェック
  - ビルドチェック: コンパイルエラー検出
  - テスト実行: 82テストケース自動実行

### テスト構成
- **テスト総数**: 82テストケース
- **テストライン数**: 3,500行超
- **カバレッジ**: 包括的検証（ユニット・統合・E2E）
- **CI環境**: Linux/macOS自動テスト実行

## F#コーディング規約

### `new`キーワード使用方針
- **IDisposableオブジェクト**: `new`キーワード必須（リソース管理明示）
- **通常のオブジェクト**: `new`キーワード省略

### 推奨パターン
- **型安全性**: Result型・Option型活用
- **エラーハンドリング**: Railway Oriented Programming
- **非同期処理**: Task・async/await正しい使用
- **関数型設計**: 不変データ構造・純粋関数優先

## 依存関係

### 必須環境
- .NET 8 SDK
- Linux または macOS
- Claude Code CLI（要インストール）

### Claude Code CLIインストール
```bash
# 公式インストール
curl -fsSL https://claude.ai/cli.sh | sh

# または npm経由
npm install -g @anthropic-ai/claude-cli
```
