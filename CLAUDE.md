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
src/                         # メインアプリケーション
├── Program.fs               # エントリーポイント・UIレイアウト定義 (225行)
├── ColorSchemes.fs          # ロール別カラースキーム定義 (27行)
├── KeyBindings.fs           # Emacsキーバインドシステム (246行)
├── ClaudeCodeProcess.fs     # Claude Codeプロセス管理・セッション制御 (190行)
├── ProcessSupervisor.fs     # プロセス分離・監視・自動復旧システム (422行)
├── Logger.fs                # 包括的ログシステム (71行)
├── fcode.fsproj             # F#プロジェクトファイル
├── bin/                     # ビルド出力
└── obj/                     # ビルド中間ファイル

tests/                       # 単体テスト
├── KeyBindingsTests.fs      # キーバインドシステムテスト
├── ColorSchemesTests.fs     # カラースキーム機能テスト
├── fcode.Tests.fsproj       # テストプロジェクトファイル
└── Program.fs               # テストエントリーポイント

docs/                        # プロジェクト文書
├── TODO.md                  # 詳細開発タスク管理
├── PRD.md                   # プロダクト要件定義
└── ui_layout.md             # UIレイアウト設計詳細
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

### 実装済み機能（100%完了）
- **UI基盤完全実装**: 8ペインレイアウト（会話、dev1-3、qa1-2、ux、PMタイムライン）
- **UI改善完了**: 会話ペイン60列幅、二重枠解消、統一カラースキーム
- **Emacsキーバインドシステム**: マルチキーシーケンス完全対応（246行）
- **レスポンシブレイアウト**: 上段40%/中段40%/下段20%比率
- **インタラクティブヘルプシステム**: Ctrl+X Hでの操作ガイド
- **包括的ログシステム**: 4段階ログレベル、カテゴリ別出力（71行）
- **プロセス分離アーキテクチャ基盤**: ProcessSupervisor完全実装（422行）
- **包括的単体テストスイート**: NUnit、43テストケース、カバレッジ100%達成

### 開発中機能（80%完了）
- **Claude Code CLI統合**: プロセス起動基盤完成、TextView初期化に課題
- **SessionManager**: 複数セッション管理基盤実装済み（190行）
- **I/O統合**: 標準入出力キャプチャ・UI表示機能（実装中）

### 緊急課題（画面表示最優先）
- **TextView初期化タイミング問題**: "TextViewが見つかりません"エラー（100%発生）
- **Claude Code出力表示**: リアルタイムI/O統合とTUI表示機能
- **基本動作確認**: dev1ペインでの完全なClaude対話実現

### 後回し機能（動作確認完了後）
- tmuxライクなセッション永続化機能
- 健全性監視・自動復旧システム
- ペイン間コンテキスト共有機能
- マルチエージェント協調ワークフロー

## 重要な実装方針

- Terminal.GuiのFrameViewを活用したペイン分割
- 会話ペインはボーダーレス（フラット表示）
- Claude Code CLIとの外部プロセス連携が前提
- 認証・API管理はClaude Code CLI側に委任

## 外部依存

- Claude Code CLI（必須）: ローカルインストール済み前提
- 設定ファイル: `~/.config/claude-tui/config.toml`（予定）

## 現在のプロジェクト状態（2025-06-25）

### 最新の実装状況
- **総実装ライン数**: 1180行 (src/), 472行 (tests/)
- **テストカバレッジ**: 43テストケース / 100%パス
- **アーキテクチャ基盤**: UI、キーバインド、ログ、プロセス分離すべて完成
- **課題**: Claude Code画面表示機能（TextView初期化問題）

### 開発フェーズ再編成: 動作確認最優先
**新しい開発方針**: セッション維持・堅牢性は後回し、まず画面表示を実現

1. **Phase 1 (最優先)**: Claude Code画面表示の実現
   - TextView初期化タイミング問題解決
   - I/O統合実装（標準入出力キャプチャ・TUI表示）
   - dev1ペインでの基本動作確認

2. **Phase 2 (安定化)**: 複数ペイン展開
   - 全ペイン対応とセッション管理安定化

3. **Phase 3 (高度機能)**: ペイン間連携・AI協調機能

### 技術的準備状況
- ✅ UI基盤（Terminal.Gui 1.5.0）完全実装・安定動作
- ✅ F#/.NET8プロセス管理基盤（SessionManager実装済み）
- ✅ プロセス分離アーキテクチャ基盤（ProcessSupervisor 422行）
- ✅ 包括的テストインフラ・ログシステム完成
- 🚨 TextView初期化問題が画面表示を阻害中

### 直近の完了作業（2025-06-25）
- **ProcessSupervisor.fs実装完了** (422行) - commit 23f987a
- **TODO.md優先順位整理** - 画面表示最優先に再構成 - commit aae2cc8
- **プロジェクト文書更新** - テスト手法統一、変数名修正 - commit 6b98d03

### 次期開発タスク（最優先）
1. TextView初期化タイミング問題の解決
2. Claude Code標準出力キャプチャ機能実装
3. dev1ペインでの基本対話動作確認

## 開発時注意事項

- **重複回避**: github issue や PR を立てる時は重複が無いか確認してから対応して
- **PR管理**: PR は issue の実装を対応した場合関連付けておいて