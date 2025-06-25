# fcode

Claude Code統合TUIエディタ - Multi-pane AI開発環境

## 概要

Claude Code CLIを統合し、複数のペインで同時にAI支援を受けながら開発できるTerminal User Interface (TUI) アプリケーションです。

## 必要環境

- .NET 8 SDK
- Linux または macOS (Windows非対応)
- TrueColor対応ターミナル（推奨: Alacritty, WezTerm, Kitty）
- 等幅フォント（Mono フォント推奨）

## インストール・実行

### 開発環境での実行

```bash
# 依存関係の復元
dotnet restore src/fcode.fsproj

# アプリケーション実行
dotnet run --project src/fcode.fsproj

# テスト実行
dotnet test tests/fcode.Tests.fsproj
```

### 本番用ビルド

```bash
# 単一ファイル実行可能形式でビルド
dotnet publish src/fcode.fsproj -c Release -r linux-x64 --self-contained true -p:PublishSingleFile=true

# 実行
./src/bin/Release/net8.0/linux-x64/publish/fcode
```

## 機能

### UIレイアウト
- **会話ペイン** (左端固定): リアルタイム会話ログ表示
- **開発者ペイン** (上段): dev1, dev2, dev3
- **QAペイン** (中段): qa1, qa2, ux  
- **PMタイムライン** (下段): 統合進捗管理

### 予定機能
- マルチペイン間でのClaude Code インスタンス並列実行
- ペイン間プロンプト連携（AIパイプライン）
- カラースキーム（Solarized配色）
- キーバインド操作（Ctrl+Tab でペイン切り替え）

## 現在の実装状況

### ✅ 実装済み
- マルチペインレイアウト表示（8ペイン構成）
- ロール別カラースキーム（dev/qa/ux/pm別配色）
- Emacsキーバインドシステム：
  - `Ctrl+X Ctrl+C`: アプリケーション終了
  - `Ctrl+X O`: 次のペインに移動
  - `Ctrl+X Ctrl+O`: 前のペインに移動
  - `Ctrl+X C`: 会話ペイン表示切替
  - `Ctrl+X 0-7`: 指定ペインに直接移動
  - `Ctrl+L`: 画面リフレッシュ
  - `Ctrl+X H`: キーバインドヘルプ表示
- 包括的単体テスト（21テスト、カバレッジ100%）

### 🚧 未実装
- Claude Code CLI統合
- プロセス間通信・AI支援機能
- 実際のコンテンツ表示

## 開発情報

- 言語: F# (.NET 8)
- UIフレームワーク: Terminal.Gui 1.5.0
- ライセンス: MIT