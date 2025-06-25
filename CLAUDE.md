# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## プロジェクト概要

Claude Code統合TUIエディタの開発プロジェクト。F#/.NET8 + Terminal.Guiを使用してクロスプラットフォームなTUIアプリケーションを構築している。

## 技術スタック

- **言語**: F# (.NET 8)
- **UIフレームワーク**: Terminal.Gui 1.5.0
- **対象プラットフォーム**: Linux, macOS (Windows対象外)
- **配布**: .NET Single File Publish (将来的にパッケージマネージャ対応予定)

## プロジェクト構造

```
src/                # メインアプリケーション
├── Program.fs      # エントリーポイント・UIレイアウト定義
├── ColorSchemes.fs # ロール別カラースキーム定義
├── KeyBindings.fs  # Emacsキーバインドシステム
├── fcode.fsproj    # F#プロジェクトファイル
├── bin/           # ビルド出力
└── obj/           # ビルド中間ファイル

tests/              # 単体テスト
├── KeyBindingsTests.fs    # キーバインドシステムテスト
├── ColorSchemesTests.fs   # カラースキーム機能テスト
├── fcode.Tests.fsproj     # テストプロジェクトファイル
└── Program.fs             # テストエントリーポイント
```

## 開発環境セットアップ

```bash
# ビルド
dotnet build src/fcode.fsproj

# 実行
dotnet run --project src/fcode.fsproj

# テスト実行
dotnet test tests/fcode.Tests.fsproj

# カバレッジレポート付きテスト
dotnet test tests/fcode.Tests.fsproj --collect:"XPlat Code Coverage"

# 単一ファイルパブリッシュ
dotnet publish src/fcode.fsproj -c Release -r linux-x64 --self-contained true -p:PublishSingleFile=true
```

## アーキテクチャ設計

### UIレイアウト構成
- **会話ペイン**: 左端固定（60列幅）、フラット表示でリアルタイム会話一覧
- **マルチペイン**: 右側エリアに開発者・QA・UX・PM用ペインを配置
- **レイアウト比率**: 上段40%(dev1-3) / 中段40%(qa1-2,ux) / 下段20%(PMタイムライン)

### 実装済み機能
- マルチペイン管理（8ペイン: 会話、dev1-3、qa1-2、ux、PMタイムライン）
- UI改善（会話ペイン60列幅、二重枠解消、統一カラースキーム）
- Emacsキーバインドシステム（マルチキーシーケンス対応）
- レスポンシブレイアウト（上段40%/中段40%/下段20%比率）
- インタラクティブヘルプシステム（Ctrl+X H）
- 包括的ログシステム（4段階ログレベル、カテゴリ別出力）
- プロセス分離アーキテクチャ基盤（ProcessSupervisor実装）
- 包括的単体テストスイート（NUnit、29+12テストケース、カバレッジ100%）

### 開発中機能
- Claude Code CLI統合の安定化（プロセス分離アーキテクチャ実装中）
- tmuxライクなセッション永続化機能
- 健全性監視・自動復旧システム

### 未実装機能
- リアルタイム会話ログ・エクスポート機能
- ロール・タグベースフィルタリング
- Broadcast/Request モード切替
- ペイン間コンテキスト共有機能

## 重要な実装方針

- Terminal.GuiのFrameViewを活用したペイン分割
- 会話ペインはボーダーレス（フラット表示）
- Claude Code CLIとの外部プロセス連携が前提
- 認証・API管理はClaude Code CLI側に委任

## 外部依存

- Claude Code CLI（必須）: ローカルインストール済み前提
- 設定ファイル: `~/.config/claude-tui/config.toml`（予定）

## 現在のプロジェクト状態（2025-01-27）

### 最新の実装状況
- **基盤UI完成**: 8ペイン構成のTUIレイアウトが完全実装済み
- **キーバインド**: Emacsスタイルのマルチキーシーケンス完全対応
- **UI改善完了**: 会話ペイン拡幅（60列）、カラー統一、二重枠解消
- **品質保証**: 21テストケースで100%カバレッジ達成

### 次期開発フェーズ: Claude Code統合
1. **Phase 1**: 単一ペイン（dev1）でのClaude Code起動機能実装
2. **Phase 2**: 複数ペイン同時セッション管理
3. **Phase 3**: ペイン間コンテキスト共有・連携機能

### 技術的準備状況
- ✅ UI基盤（Terminal.Gui 1.5.0）安定動作
- ✅ F#/.NET8での外部プロセス管理準備
- ✅ 包括的テストインフラ構築済み
- 🔄 Claude Code CLIプロセス統合設計中（TODO.md参照）

### 直近の完了作業
- UI改善（会話ペイン拡幅、二重枠解消、カラー統一） - commit ffad907
- 単体テスト整備（21テストケース、カバレッジ100%） - commit 5bf1db8  
- Emacsキーバインド実装（マルチキーシーケンス対応） - commit 859287b