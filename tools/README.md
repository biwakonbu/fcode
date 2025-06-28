# Tools Directory

開発・品質向上・自動化ツール集

## 構造

```
tools/
├── README.md                    # このファイル
└── fsac-auto-fix/              # FSAC診断警告自動修正ツール
    ├── README.md               # ツール詳細ドキュメント
    ├── fsac-auto-fix.fsx       # メインツール
    ├── *.md                    # 関連ドキュメント
    ├── tests/                  # テストスイート
    │   ├── run-all-tests.sh    # 全テスト実行
    │   ├── quick-test.fsx      # 簡易テスト
    │   ├── problem-cases-test.fsx  # 問題ケース分析
    │   └── fsac-auto-fix-tests.fsx # 包括的テスト（構文エラーあり）
    └── samples/                # テスト用サンプル
        └── test-sample.fs      # 修正テスト用サンプル
```

## 利用可能なツール

### FSAC Auto-Fix Tool

F# FSAC診断警告を自動修正するツール。コード品質向上とFSAC警告削減を目的としています。

**主な機能:**
- 不要な括弧の自動削除（`func(arg)` → `func arg`）
- IDisposableコンストラクタの保護
- メソッドチェーン優先順位問題の回避
- 段階的修正アプローチ（Conservative/Standard/Aggressive）

**使用例:**
```bash
cd fsac-auto-fix
dotnet fsi fsac-auto-fix.fsx -- --dir ../src --level conservative --dry-run
```

**詳細:** `fsac-auto-fix/README.md` を参照

## ツール開発の原則

### ディレクトリ構造の規則

1. **ツール毎にディレクトリ分離**
   - メインツール・テスト・ドキュメントを1つのディレクトリに集約
   - `tool-name/` の形式でディレクトリ作成

2. **標準サブディレクトリ**
   - `tests/` - テストファイル（実行可能なテストスクリプト）
   - `samples/` - テスト・動作確認用サンプルファイル
   - `docs/` - 詳細ドキュメント（必要に応じて）

3. **ファイル命名規則**
   - メインツール: `tool-name.fsx` または `tool-name.sh`
   - テスト: `test-*.fsx` または `*-test.fsx`
   - 統合テスト: `run-all-tests.sh`
   - README: `README.md`（各ディレクトリに配置）

### 品質基準

1. **テストの必須化**
   - すべてのツールに対応するテストスイート作成
   - 簡易テスト（`quick-test.fsx`）で基本動作確認
   - 問題ケース分析（`problem-cases-test.fsx`）で精度確認

2. **ドキュメント整備**
   - 使用方法・オプション・例を明記
   - 継続的改善プロセスの説明
   - トラブルシューティングガイド

3. **継続的改善**
   - 問題発見時のテストケース追加プロセス
   - ツール精度向上のための分析機能
   - バージョン管理とリリースノート

## 将来の拡張予定

- **Code Formatting Tools** - F#コードフォーマッター統合
- **Build Automation** - CI/CD統合スクリプト
- **Quality Analysis** - コード品質分析ツール
- **Documentation Generators** - 自動ドキュメント生成

## 貢献ガイドライン

新しいツールを追加する場合：

1. `tools/tool-name/` ディレクトリ作成
2. メインツール（`.fsx` または `.sh`）配置
3. `tests/` ディレクトリにテストスイート作成
4. `README.md` でドキュメント整備
5. `tools/README.md`（このファイル）に概要追加