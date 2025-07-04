# fcode - AI Team Collaboration TUI

**Claude Code + Multi-Agent AI Development Environment**

fcodeは、Claude Code CLIを核とした**AIチーム協働開発環境**です。複数のAIエージェントが役割分担して協調し、「ざっくり指示→20分自走→完成確認」のワークフローを実現するTerminal UIアプリケーションです。

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

### 前提条件
```bash
# 必須環境
- .NET 8 SDK
- Linux または macOS (Windows非対応)
- Claude Code CLI (要インストール)

# Claude Code CLIインストール
curl -fsSL https://claude.ai/cli.sh | sh
# または
npm install -g @anthropic-ai/claude-cli
```

### インストール手順
```bash
# リポジトリクローン
git clone <repository-url>
cd fcode

# 開発環境セットアップ
make setup

# アプリケーション実行
dotnet run --project src/fcode.fsproj

# または製品版ビルド
make release
./publish/linux-x64/fcode
```

## ⌨️ 基本操作

### キーバインド (Emacs風)
| キー | 機能 | 説明 |
|---|---|---|
| `Ctrl+X Ctrl+C` | アプリケーション終了 | 全セッション安全終了 |
| `Ctrl+X O` | 次ペイン移動 | 順次ペイン切り替え |
| `Ctrl+X 0-8` | 直接ペイン移動 | 会話/dev1-3/qa1-2/ux/pm/pdm |
| `Ctrl+X S` | Claude Code起動 | 現在ペインでセッション開始 |
| `Ctrl+X K` | Claude Code終了 | 現在ペインでセッション停止 |
| `Ctrl+X H` | ヘルプ表示 | 操作ガイド・キーバインド一覧 |

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

## 🎯 プロジェクト目標・成功指標

### FC-014実装計画の成功基準

#### ミニマム成功基準 (必達)
- ✅ **Claude Code統合100%完成**: POが実際に開発で使用可能
- ✅ **追加CLI統合1つ以上**: 動作実証・拡張性確認
- ✅ **基本協調機能動作**: エージェント間作業分担・進捗共有
- ✅ **20分自走フロー**: ざっくり指示→自動作業→完成確認の基本動作

#### 理想的成功基準 (目標)
- 🎯 **複数エージェント協調**: 3-5つのCLIツール同時運用
- 🎯 **自動品質保証**: pdm判断・pm作業移譲の完全自動化
- 🎯 **高度エスカレーション**: 致命度評価・PO判断の精度向上
- 🎯 **実用レベル完成度**: 実際のプロダクト開発での継続使用可能性

## 📚 ドキュメント

- [GRAND_DESIGN.md](docs/GRAND_DESIGN.md) - 全体設計・技術アーキテクチャ・実装計画
- [USER_MANUAL.md](docs/USER_MANUAL.md) - 利用者向け操作マニュアル・ワークフロー
- [TODO.md](TODO.md) - 開発進捗・実装状況・次期タスク
- [PRD.md](PRD.md) - プロダクト要件定義・ユースケース

## 🤝 Contributing

このプロジェクトは**AIチーム協働開発環境**の実現を目指しています。Issues・Pull Requestを通じた貢献を歓迎します。

### 品質ポリシー
- すべてのコミットでpre-commitフック通過必須
- テストカバレッジ80%以上維持
- F#コードフォーマット準拠 (Fantomas)
- コミットメッセージ: `FC-XXX 機能名: 簡潔な説明`

## 📄 ライセンス

MIT License - 詳細は[LICENSE](LICENSE)を参照

---

**fcodeで実現する未来**: POが技術的制約から解放され、純粋にプロダクト価値創造に集中できるAI協働開発環境