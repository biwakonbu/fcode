# PoC 開発 TODO

本リポジトリで **TUI PoC** を立ち上げるために、直近で対応すべき作業タスクと参照ドキュメントを整理しました。

---

## 1. 完了済みタスク ✅

- [x] **開発環境セットアップ**
  - .NET 8 SDK がインストール済みか確認 (`dotnet --version`) → 8.0.101で確認済み
  - Mono フォント & TrueColor 対応ターミナル（推奨: Alacritty, WezTerm, Kitty）
- [x] **依存ライブラリの復元**
  - `dotnet restore src/fcode.fsproj` → Terminal.Gui 1.5.0復元済み
- [x] **PoC 起動確認**
  - `dotnet run --project src/fcode.fsproj` → ビルド・起動成功
  - 画面レイアウトが **docs/ui_layout.md** と一致しているか目視確認 → 8ペイン構成実装済み
- [x] **README 更新**
  - 実行方法・フォント設定・既知の問題を追記 → 実装状況含め更新済み
- [x] **ペイン間レイアウトの微調整**
  - 罫線の有無 / タイトル文字列の幅を UI デザインに合わせて修正 → 完了
  - `会話ペイン` をフラット表示にしているか再確認 → フラット表示実装済み
- [x] **カラースキーム適用**
  - `docs/ui_layout.md` 4 章「カラースキーム」を適用 → ロール別配色実装済み
  - Terminal.Gui の `ColorScheme` を中央集約するモジュールを新規作成 → ColorSchemes.fs作成済み
- [x] **フォーカス操作 (Ctrl+Tab) 実装調査**
  - Terminal.Gui の `View.NextView` / `FocusNext` をラップしてキーバインド実装 → Ctrl+Tab/Ctrl+C実装済み

## 2. 緊急修正タスク (即座対応)

- [ ] **終了機能実装**
  - アプリケーションが正常終了できない問題の修正
  - Escキーまたは適切な終了キーバインドの追加
  - Application.RequestStop()の適切な呼び出し

## 3. 次期開発タスク (機能実装着手前)

- [ ] **設定ファイル仕様策定**
  - 位置: `~/.config/claude-tui/config.toml`
  - 保存項目: Claude Code CLI のパス、デフォルトレイアウト preset など
- [ ] **CLI 呼び出し方針決定**
  - `System.Diagnostics.Process` でラップ or 標準入出力を TUI へストリーム転送
- [ ] **テスト方針のブレークダウン**
  - PRD 3.「QA 方針」に沿ってヒューリスティックテスト観点を洗い出す

---

## 参照ドキュメント

| 資料 | 主な内容 | 必読度 |
|------|----------|-------|
| `PRD.md` | プロダクト全体要件、チーム体制、技術選定 | ★★★ |
| `docs/ui_layout.md` | PoC で再現すべき画面レイアウト詳細、カラースキーム | ★★★ |
| `README.md` | ビルド & 実行方法（今後追記） | ★★☆ |
| ライブラリ: Terminal.Gui | UI フレームワークの API ドキュメント | ★★☆ |
| .NET 8 Docs | F# / .NET の基礎 & `Single-file publish` 方法 | ★☆☆ |

> **メモ**: タスクは随時 GitHub Issues に移管予定。今後は Issue + Project ボードで管理する。 
