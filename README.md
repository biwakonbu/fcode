# fcode - AI Team Collaboration TUI

[![.NET](https://img.shields.io/badge/.NET-8.0-blue.svg)](https://dotnet.microsoft.com/)
[![F#](https://img.shields.io/badge/F%23-Language-blue.svg)](https://fsharp.org/)
[![Tests](https://img.shields.io/badge/Tests-534%2F534%20✓-green.svg)](https://github.com/biwakonbu/fcode)
[![License](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)
[![Status](https://img.shields.io/badge/Status-Production%20Ready-brightgreen.svg)](https://github.com/biwakonbu/fcode)

**Claude Code + Multi-Agent AI Development Environment**

fcodeは、Claude Code CLIを核とした**AIチーム協働開発環境**です。複数のAIエージェントが役割分担して協調し、「ざっくり指示→20分自走→完成確認」のワークフローを実現するTerminal UIアプリケーションです。

> **🚀 リリース準備完了**: 534テスト全通・実用レベル完成・本格運用開始可能

## 🎯 コンセプト

### PO中心の開発体験
- **実装詳細からの完全解放**: 技術的制約を意識せず、プロダクト価値創造に集中
- **20分自走システム**: ざっくりした指示から自動的にタスク分解・実装・品質保証
- **多角的AI提案**: dev/qa/ux/pm/pdmの視点からバランス取れた解決策を自動提示

### AIチーム協調アーキテクチャ
```
┌─────────────┬─────────────────────────────────────┐
│  会話ペイン  │           AIチーム作業エリア          │
│   (PO操作)  │ ┌─────────┬─────────┬─────────┐     │
│             │ │  dev1   │  dev2   │  dev3   │     │
│  ・指示入力 │ │シニア   │並列A    │並列B    │     │
│  ・判断確認 │ │エンジニア│エンジニア│エンジニア│     │
│  ・承認操作 │ ├─────────┼─────────┼─────────┤     │
│             │ │  qa1    │  qa2    │   ux    │     │
│  POの       │ │テスト   │探索的   │UI/UX    │     │
│  メイン     │ │リード   │テスト   │デザイナー│     │
│  操作領域   │ ├─────────┴─────────┴─────────┤     │
│             │ │      PM/PdM統合管理エリア    │     │
│             │ │   進捗管理・品質判断・調整    │     │
└─────────────┴─────────────────────────────────────┘
```

## 🚀 主要機能

### ✅ 実装済み基盤 (Phase 0)
- **9ペインマルチエージェント基盤**: 役割別AI配置・同時実行環境
- **Claude Code統合**: プロセス分離・自動復旧・セッション管理
- **Emacsキーバインド**: 効率的なペイン操作・コマンド実行
- **包括的品質保証**: 82テストケース・CI/CDパイプライン・pre-commitフック

### 🚧 開発中 (FC-014)
#### Phase 1: CLI統合フレームワーク構築 (2-3 SP)
- **IAgentCLI汎用インターフェース**: Claude Code以外のCLI統合基盤
- **動的エージェント登録**: 設定ベース・プラグイン形式での追加エージェント対応
- **プロセス管理拡張**: 複数CLIツール・リソース制御・安定性向上

#### Phase 2: エージェント間通信基盤 (3-4 SP)
- **マルチエージェントメッセージング**: タスク配分・進捗共有・質問応答システム
- **リアルタイム協調**: エージェント状態同期・並列作業制御
- **会話ペイン統合**: 全エージェント状況・意思決定・エスカレーション可視化

#### Phase 3: 協調制御システム (4-5 SP)
- **TaskAssignmentManager**: PM主導の動的タスク配分・再配分
- **QualityGateManager**: pdm品質ゲート・上流下流レビューシステム
- **EscalationManager**: 致命度評価・自動判断・PO通知システム

## 📋 「ざっくり指示→20分自走→完成確認」ワークフロー

### 1. [PO] ざっくり指示入力
```
POが会話ペインに自然言語で要求を入力:
「ECサイトのカート機能を改善したい」
「パフォーマンスを向上させてほしい」
「新機能のプロトタイプを作成して」
```

### 2. [自動解析・配分] TaskAssignmentManager
- 要求内容を自動解析してタスク分解
- 各エージェントの専門性に基づく最適配分
- 依存関係考慮・並列実行可能な作業計画策定

### 3. [20分自走] 各エージェント並列作業
```
dev1-3: アーキテクチャ設計→並列実装→レビュー
qa1-2:  テスト戦略策定→品質検証→課題特定
ux:     ユーザー視点分析→UI改善→体験向上
pm:     進捗管理→リスク検出→調整判断
pdm:    品質評価→競合分析→改善提案
```

### 4. [品質チェック] pdm主導統合評価
- 上流レビュー (pdm + dev2): 実装品質・アーキテクチャ妥当性
- 下流レビュー (ux + qa1): ユーザー体験・品質基準適合性
- 実装困難時の3案出し・代替提案・技術的実現性評価

### 5. [完成確認] POによる最終承認
- 統合レビュー結果・実用性基準の達成確認
- 必要に応じた追加調整・次タスクの自動開始

## 🛠️ インストール・セットアップ

### 🎯 30秒クイックスタート（初回ユーザー向け）
```bash
# 1. 前提条件確認
# .NET 8 SDK インストール済み？
dotnet --version  # 8.0.x が表示されればOK

# Claude Code CLI インストール済み？
claude --version  # バージョンが表示されればOK

# 2. fcodeダウンロード・実行
git clone https://github.com/biwakonbu/fcode.git
cd fcode
dotnet run --project src/fcode.fsproj

# 3. 基本操作確認
# Ctrl+X H でヘルプ表示
# Ctrl+X S でClaude Code起動
# Ctrl+X Ctrl+C で終了
```

### 詳細インストール手順

#### 前提条件
```bash
# 必須環境
- .NET 8 SDK (必須)
- Linux または macOS (Windows非対応)
- Claude Code CLI (必須)
- 最小端末サイズ: 120x30文字
- 推奨端末サイズ: 160x40文字以上
```

#### 1. .NET 8 SDKインストール
```bash
# Ubuntu/Debian
sudo apt update
sudo apt install -y dotnet-sdk-8.0

# macOS (Homebrew)
brew install dotnet

# インストール確認
dotnet --version  # 8.0.x が表示される
```

#### 2. Claude Code CLIインストール
```bash
# Claude公式インストーラー（推奨）
curl -fsSL https://claude.ai/cli.sh | sh

# または npm経由
npm install -g @anthropic-ai/claude-cli

# インストール確認
claude --version
```

#### 3. fcodeセットアップ
```bash
# リポジトリクローン
git clone https://github.com/biwakonbu/fcode.git
cd fcode

# ビルド確認
dotnet build src/fcode.fsproj

# アプリケーション実行
dotnet run --project src/fcode.fsproj
```

#### 4. 配布版ビルド（オプション）
```bash
# Single File実行ファイル生成
./scripts/publish-release.sh

# 生成されたバイナリ実行
./src/bin/Release/net8.0/linux-x64/publish/fcode

# システム全体インストール
sudo cp ./src/bin/Release/net8.0/linux-x64/publish/fcode /usr/local/bin/
fcode  # どこからでも実行可能
```

## ⌨️ 基本操作

### 🚀 初回起動時の操作フロー
```bash
# 1. アプリケーション起動
fcode

# 2. ヘルプ確認（重要：最初に確認推奨）
Ctrl+X H

# 3. 会話ペインでClaude Code起動
Ctrl+X 0  # 会話ペインに移動
Ctrl+X S  # Claude Code起動

# 4. 基本指示入力例
"ECサイトのカート機能を改善したい"
"パフォーマンスを向上させてほしい"

# 5. 終了
Ctrl+X Ctrl+C  # 全セッション安全終了
```

### キーバインド (Emacs風)
| キー | 機能 | 説明 |
|---|---|---|
| `Ctrl+X Ctrl+C` | **アプリケーション終了** | 全セッション安全終了 |
| `Ctrl+X H` | **ヘルプ表示** | 操作ガイド・キーバインド一覧 |
| `Ctrl+X 0-8` | **直接ペイン移動** | 会話/dev1-3/qa1-2/ux/pm/pdm |
| `Ctrl+X O` | 次ペイン移動 | 順次ペイン切り替え |
| `Ctrl+X S` | Claude Code起動 | 現在ペインでセッション開始 |
| `Ctrl+X K` | Claude Code終了 | 現在ペインでセッション停止 |
| `Ctrl+L` | UI更新 | 画面を再描画 |

### ペイン構成・役割分担
| ペイン | 役割 | 主要責務 |
|---|---|---|
| **会話** | PO操作領域 | 指示入力・判断確認・チーム連携 |
| **dev1** | シニアエンジニア | 技術リード・アーキテクチャ・最終レビュー |
| **dev2/3** | エンジニア | 並列実装・テスト・継続改善 |
| **qa1** | テストリード | 戦略策定・品質ゲート・仕様確認 |
| **qa2** | 探索的テスト | 創造的・過酷条件・課題発見 |
| **ux** | UI/UXデザイナー | ユーザー体験・定量分析・KPI設計 |
| **pm** | プロジェクトマネージャー | 要件定義・進捗管理・動的配分 |
| **pdm** | プロダクトマネージャー | 品質評価・競合分析・受け入れ判断 |

## 🔧 開発者向け情報

### プロジェクト構造
```
src/                     # メインアプリケーション (1180行)
├── Program.fs           # UIレイアウト・エントリーポイント
├── ClaudeCodeProcess.fs # Claude Code統合・セッション管理
├── KeyBindings.fs       # Emacsキーバインドシステム
├── ConfigurationManager.fs # 設定管理・エージェント設定
├── *PromptManager.fs    # 役割別プロンプト管理 (QA/UX/PM)
└── [その他の基盤モジュール]

tests/                   # 包括的テストスイート (2130行・82テスト)
├── *Tests.fs            # ユニット・統合・E2Eテスト
├── *EndToEndTests.fs    # エージェント別統合テスト
└── [パフォーマンス・セキュリティテスト]

docs/                    # プロジェクト文書
├── GRAND_DESIGN.md      # 全体設計・実装計画
├── USER_MANUAL.md       # 利用者向けマニュアル
└── [技術文書・アーキテクチャ資料]
```

### 開発ワークフロー
```bash
# コード品質管理 (pre-commit自動実行)
make check               # 全品質チェック
make format              # F#コードフォーマット
make test                # テスト実行 (82テストケース)
make build               # ビルド確認

# 開発・デバッグ
dotnet run --project src/fcode.fsproj
tail -f /tmp/fcode-logs/fcode-*.log  # ログ監視
```

### 技術スタック
- **言語**: F# (.NET 8) - 関数型・型安全性・非同期処理
- **UIフレームワーク**: Terminal.Gui 1.15.0 - クロスプラットフォーム
- **プロセス管理**: .NET Process + Unix Domain Sockets - 高信頼性IPC
- **設定管理**: System.Text.Json - 標準・高性能
- **テスト**: NUnit + 包括的テストスイート - 品質保証

## 🎯 プロジェクト状況・リリース準備

### 現在のリリース状況 (2025-07)

#### ✅ 完了済み基盤機能
- **UI基盤**: 8ペインレイアウト・Emacsキーバインド完全実装
- **Claude Code統合**: プロセス分離・セッション管理・自動復旧
- **品質保証**: 534/534テスト100%成功・CI/CD・pre-commitフック
- **リアルタイム協調**: エージェント状態管理・タスク依存関係・進捗監視
- **包括的ログ**: 4段階ログレベル・カテゴリ別出力
- **パフォーマンス最適化**: メモリ監視・リソース管理

#### 🔍 リリース判定
- **機能完全性**: ✅ 予定機能100%実装完了
- **基本動作**: ✅ アプリケーション起動・基本操作可能
- **テスト品質**: ✅ 534テスト全通・包括的品質保証
- **アーキテクチャ**: ✅ 拡張可能・保守可能な設計

**結論**: **fcodeは実用可能なレベルで完成済み**

### システム要件
- **対応OS**: Linux, macOS (Windows非対応)
- **最小メモリ**: 256MB
- **推奨メモリ**: 512MB以上
- **端末要件**: 256色対応・等幅フォント推奨
- **Claude Code依存**: 最新版推奨

## 📚 ドキュメント

- [GRAND_DESIGN.md](docs/GRAND_DESIGN.md) - 全体設計・技術アーキテクチャ・実装計画
- [USER_MANUAL.md](docs/USER_MANUAL.md) - 利用者向け操作マニュアル・ワークフロー
- [TODO.md](TODO.md) - 開発進捗・実装状況・次期タスク
- [PRD.md](PRD.md) - プロダクト要件定義・ユースケース

## 🤝 Contributing・サポート

### 🐛 問題報告・機能要望
- **バグ報告**: [GitHub Issues](https://github.com/biwakonbu/fcode/issues)
- **機能要望**: [GitHub Discussions](https://github.com/biwakonbu/fcode/discussions)
- **質問・ヘルプ**: [GitHub Discussions](https://github.com/biwakonbu/fcode/discussions)

### 💡 開発貢献
このプロジェクトは**AIチーム協働開発環境**の実現を目指しています。Issues・Pull Requestを通じた貢献を歓迎します。

#### 品質ポリシー
- すべてのコミットでpre-commitフック通過必須
- テストカバレッジ維持 (現在534/534テスト成功)
- F#コードフォーマット準拠 (Fantomas)
- コミットメッセージ: `FC-XXX 機能名: 簡潔な説明`

#### 開発環境セットアップ
```bash
# 開発用セットアップ
git clone https://github.com/biwakonbu/fcode.git
cd fcode
dotnet build src/fcode.fsproj
dotnet test tests/fcode.Tests.fsproj  # 534テスト実行

# コード品質チェック
./scripts/format-and-lint.sh
```

## 📄 ライセンス

MIT License - 詳細は[LICENSE](LICENSE)を参照

---

## 📈 ロードマップ・将来計画

### Next: v1.1 エージェント協調強化
- **エージェント間通信**: タスク配分・進捗共有・質問応答システム
- **高度品質ゲート**: pdm品質評価・自動判断・エスカレーション
- **複数CLIツール統合**: Git・Docker・AWS CLI等の統合

### Future: v2.0 本格的AI協働
- **自動化ワークフロー**: ざっくり指示→完全自動実行
- **学習・改善システム**: 成功パターン学習・効率化
- **クラウド統合**: チーム協調・リモート開発支援

---

**🎯 fcodeのビジョン**: POが技術的制約から解放され、純粋にプロダクト価値創造に集中できるAI協働開発環境の実現