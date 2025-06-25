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

### 開発環境セットアップ

```bash
# リポジトリクローン
git clone https://github.com/biwakonbu/fcode.git
cd fcode

# 開発環境の初期セットアップ (Git hooks + ツール)
make setup

# または手動で設定
./.githooks/setup.sh
```

### 開発環境での実行

```bash
# 依存関係の復元
dotnet restore src/fcode.fsproj

# アプリケーション実行
dotnet run --project src/fcode.fsproj

# テスト実行
make test
# または
dotnet test tests/fcode.Tests.fsproj
```

### 本番用ビルド

```bash
# リリースビルド + 単一ファイルパブリッシュ
make release

# または手動で
dotnet publish src/fcode.fsproj -c Release -r linux-x64 --self-contained true -p:PublishSingleFile=true -o publish/linux-x64

# 実行
./publish/linux-x64/fcode
```

## 開発ワークフロー

### コード品質管理

プロジェクトにはpre-commitフックが設定されており、コミット前に自動で品質チェックが実行されます：

```bash
# 手動でのコード品質チェック
make check

# 個別の品質チェック
make format    # F#コードフォーマット
make lint      # リント実行
make test      # テスト実行
make build     # ビルド確認
```

**Pre-commitフックの動作**：
1. 📝 **フォーマットチェック**: Fantomasによる自動コードフォーマット
2. 🔍 **リント**: 警告をエラーとして扱う厳格なリント
3. 🧪 **テスト**: 全テストケースの実行確認
4. 🏗️ **ビルド**: リリース構成でのビルド確認

**🚨 重要な品質ポリシー**：
- `--no-verify`フラグの使用は**禁止**されています
- 品質チェックをスキップしようとするとコミットが**強制的に失敗**します
- すべてのエラーを修正してからコミットする必要があります

### 利用可能なMakeコマンド

```bash
make help          # 全コマンドの一覧表示
make setup         # プロジェクト初期セットアップ
make check         # 全品質チェック実行
make format        # コードフォーマット
make lint          # リント実行
make test          # テスト実行
make build         # デバッグビルド
make release       # リリースビルド
make clean         # ビルド成果物削除
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
- UI改善（会話ペイン60列幅、統一カラースキーム、二重枠解消）
- Emacsキーバインドシステム：
  - `Ctrl+X Ctrl+C`: アプリケーション終了
  - `Ctrl+X O`: 次のペインに移動
  - `Ctrl+X Ctrl+O`: 前のペインに移動
  - `Ctrl+X C`: 会話ペイン表示切替
  - `Ctrl+X S`: 現在ペインでClaude Code起動/再起動
  - `Ctrl+X K`: 現在ペインのClaude Code終了
  - `Ctrl+X 0-7`: 指定ペインに直接移動
  - `Ctrl+L`: 画面リフレッシュ
  - `Ctrl+X H`: キーバインドヘルプ表示
- Claude Code統合基盤（各エージェントペインでの自動起動）
- 包括的ログシステム（詳細なデバッグ情報出力）
- 包括的単体テスト（29テストケース、カバレッジ100%）

### 🚧 開発中
- Claude Code CLI統合の安定化
- プロセス間通信・AI支援機能の最適化
- ペイン間コンテキスト共有機能

## ログシステム

### ログ出力
アプリケーションの動作は詳細にログ記録されます：

- **ログ出力先**: `/tmp/fcode-logs/fcode-{timestamp}.log`
- **ログレベル**: DEBUG, INFO, WARN, ERROR
- **ログカテゴリ**: Application, UI, AutoStart, SessionManager, KeyBindings

### ログの確認方法
```bash
# 最新のログファイルを確認
ls -lat /tmp/fcode-logs/

# ログをリアルタイム監視
tail -f /tmp/fcode-logs/fcode-*.log

# エラーのみ抽出
grep ERROR /tmp/fcode-logs/fcode-*.log
```

### トラブルシューティング

#### Claude Code自動起動の問題
現在、TextViewの初期化タイミングの問題により、自動起動が失敗することがあります：

- **症状**: "TextViewが見つかりません" エラー
- **原因**: UI要素の初期化順序とTerminal.Guiのレンダリングタイミング
- **対策**: アプリケーション起動後に手動で `Ctrl+X S` で各ペインのClaude Codeを起動

#### ログファイルが見つからない場合
```bash
# ログディレクトリの確認
ls -la /tmp/fcode-logs/

# 権限問題の場合
chmod 755 /tmp/fcode-logs/
```

## 開発情報

- 言語: F# (.NET 8)
- UIフレームワーク: Terminal.Gui 1.5.0
- ライセンス: MIT# test change
