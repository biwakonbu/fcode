# FSAC Auto-Fix Tool - 安全性設定ガイド

## 安全性レベル別の動作

### Conservative（最も安全）
**適用パターン**: 
- ✅ 静的メソッド呼び出し（`Directory.Exists(path)` → `Directory.Exists path`）
- ✅ 型キャスト関数（`float(x)` → `float x`、複雑式は括弧保持）
- ✅ パイプライン内関数呼び出し
- ✅ 冗長な修飾子除去（`System.String` → `String`）

**除外パターン**:
- ❌ 単純な関数呼び出し（IDisposableリスクあり）
- ❌ インスタンスメソッド呼び出し（プロパティチェーンリスクあり）
- ❌ メソッドチェーン優先順位調整
- ❌ 未使用open文削除

### Standard（バランス重視）
**追加パターン**:
- ✅ 単純な関数呼び出し（安全性チェック付き）

**安全性チェック**:
- 文字列・コメント内除外
- IDisposableコンストラクタ保護
- プロパティチェーン検出時除外
- キーワード周辺（match, if, let等）除外

### Aggressive（全機能）
**追加パターン**:
- ⚠️ インスタンスメソッド呼び出し（リスクあり）
- ⚠️ メソッドチェーン優先順位調整（リスクあり）
- ⚠️ 未使用open文削除（手動確認推奨）

## 自動除外される危険なケース

### 1. 文字列・コメント内
```fsharp
// 除外される
let text = "function(test)"
// Call function(arg)
```

### 2. プロパティチェーン
```fsharp
// 除外される（優先順位問題のリスク）
someObject.Method(arg).Property
```

### 3. 複雑な式
```fsharp
// 除外される（演算子優先順位のリスク）
let result = func(value + offset)
let complex = method(if condition then a else b)
```

### 4. キーワード周辺
```fsharp
// 除外される（構文解析が困難）
match func(value) with
if condition(arg) then
```

### 5. IDisposableコンストラクタ
```fsharp
// 除外される（リソース管理の明示性重要）
let frame = new FrameView(title)
let stream = new FileStream(path)
```

### 6. ネストした括弧
```fsharp
// 除外される（複雑度が高い）
let result = outer(inner(nested(value)))
```

## 推奨使用フロー

1. **Conservative レベルでドライラン**
   ```bash
   dotnet fsi fsac-auto-fix.fsx -- --dir src --level conservative --dry-run
   ```

2. **安全な変更のみ適用**
   ```bash
   dotnet fsi fsac-auto-fix.fsx -- --dir src --level conservative
   ```

3. **Standard レベルで追加確認**
   ```bash
   dotnet fsi fsac-auto-fix.fsx -- --dir src --level standard --dry-run
   ```

4. **必要に応じて部分適用**
   ```bash
   # 特定の診断のみ
   dotnet fsi fsac-auto-fix.fsx -- --dir src --fix FSAC0004 --dry-run
   ```

## 安全性チェック関数

### `isInStringOrComment`
- エスケープ文字を考慮した文字列検出
- 行コメント（`//`）の検出
- エラー時は安全側に倒してスキップ

### `hasComplexExpression`
- 演算子（`+`, `-`, `*`, `/`, `&&`, `||`, `=`, `<`, `>`）
- ドット記法（メソッド・プロパティアクセス）
- 制御構文（`if`, `match`, `let`）
- 区切り文字（`,`, `[`, `{`）

### `hasPropertyChain`
- マッチ後20文字以内にドット記法があるかチェック
- プロパティチェーンによる優先順位問題を防止

### `isNearKeyword`
- マッチ前20文字以内のキーワード検出
- 制御構文での誤変換を防止

## エラー処理

すべての安全性チェック関数は例外時に `true`（スキップ）を返し、安全側に倒します。
不明なケースでは変換せず、手動確認を促します。