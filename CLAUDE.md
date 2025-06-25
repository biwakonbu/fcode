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
├── fcode.fsproj    # F#プロジェクトファイル
├── bin/           # ビルド出力
└── obj/           # ビルド中間ファイル
```

## 開発環境セットアップ

```bash
# ビルド
dotnet build src/fcode.fsproj

# 実行
dotnet run --project src/fcode.fsproj

# 単一ファイルパブリッシュ
dotnet publish src/fcode.fsproj -c Release -r linux-x64 --self-contained true -p:PublishSingleFile=true
```

## アーキテクチャ設計

### UIレイアウト構成
- **会話ペイン**: 左端固定（20列幅）、フラット表示でリアルタイム会話一覧
- **マルチペイン**: 右側エリアに開発者・QA・UX・PM用ペインを配置
- **レイアウト比率**: 上段40%(dev1-3) / 中段40%(qa1-2,ux) / 下段20%(PMタイムライン)

### 実装済み機能
- マルチペイン管理（8ペイン: 会話、dev1-3、qa1-2、ux、PMタイムライン）
- ロール別カラースキーム（dev/qa/ux/pm別配色）
- Emacsキーバインドシステム（マルチキーシーケンス対応）
- レスポンシブレイアウト（上段40%/中段40%/下段20%比率）
- インタラクティブヘルプシステム（Ctrl+X H）

### 未実装機能
- Claude Code CLI統合による AI支援
- リアルタイム会話ログ・エクスポート機能
- ロール・タグベースフィルタリング
- Broadcast/Request モード切替

## 重要な実装方針

- Terminal.GuiのFrameViewを活用したペイン分割
- 会話ペインはボーダーレス（フラット表示）
- Claude Code CLIとの外部プロセス連携が前提
- 認証・API管理はClaude Code CLI側に委任

## 外部依存

- Claude Code CLI（必須）: ローカルインストール済み前提
- 設定ファイル: `~/.config/claude-tui/config.toml`（予定）