# FSAC Auto-Fix Tool

F# FSAC診断警告を自動修正するツール

## ディレクトリ構造

```
fsac-auto-fix/
├── README.md                           # このファイル
├── fsac-auto-fix.fsx                   # メインツール
├── fsac-auto-fix-improvements.md       # 改良レポート
├── fsac-auto-fix-testing-guide.md      # テスト・改善ガイド
├── fsac-auto-fix-usage-examples.md     # 使用例・トラブルシューティング
├── tests/                              # テストファイル
│   ├── run-all-tests.sh                # 全テスト実行スクリプト
│   ├── fsac-auto-fix-tests.fsx         # 包括的テストスイート
│   ├── quick-test.fsx                  # 簡易テスト
│   └── problem-cases-test.fsx          # 問題ケース分析ツール
└── samples/                            # テスト用サンプルファイル
    └── test-sample.fs                  # 修正テスト用サンプル
```

## 使用方法

### 基本的な使用

```bash
# ヘルプ表示
dotnet fsi fsac-auto-fix.fsx -- --help

# サポートされている診断コード確認
dotnet fsi fsac-auto-fix.fsx -- --diagnostics

# ドライラン（プレビューのみ）
dotnet fsi fsac-auto-fix.fsx -- --dir ../../src --dry-run

# Conservative レベルで修正
dotnet fsi fsac-auto-fix.fsx -- --dir ../../src --level conservative

# 特定の診断コードのみ修正
dotnet fsi fsac-auto-fix.fsx -- --dir ../../src --fix FSAC0004
```

### 対応する診断コード

- **FSAC0004**: 不要な括弧の除去 (`func(arg)` → `func arg`)
- **FSAC0002**: 冗長な修飾子の削除 (`System.String` → `String`)
- **FSAC0001**: 未使用open文の削除

### 安全性レベル

- **Conservative**: 最も安全（型キャスト、静的メソッドのみ）
- **Standard**: バランス重視（IDisposableコンストラクタ保護）
- **Aggressive**: 全修正（未使用open文含む）

## テスト実行

### 全テストの一括実行
```bash
cd tests
./run-all-tests.sh
```

### 個別テストの実行

#### 簡易テスト（基本動作確認）
```bash
cd tests
dotnet fsi quick-test.fsx
```

#### 問題ケース分析
```bash
cd tests
dotnet fsi problem-cases-test.fsx
```

#### 包括的テストスイート（構文エラーにより一時無効）
```bash
cd tests
# dotnet fsi fsac-auto-fix-tests.fsx  # 現在構文エラーあり
```

## 改良プロセス

1. 問題発見時は `tests/` ディレクトリにテストケース追加
2. メインツール (`fsac-auto-fix.fsx`) を修正
3. テスト実行して検証
4. 本番適用

## 特徴

- **IDisposable保護**: `new FrameView(title)` などの `new` キーワード保持
- **優先順位保護**: `Directory.GetFiles(dir).Length` → `(Directory.GetFiles dir).Length`
- **複雑式判定**: `float(a + b)` → `float (a + b)` のように括弧保持
- **段階的適用**: Conservative → Standard → Aggressive の安全なアプローチ