# FSAC Auto-Fix Tool - 既知の制限事項と対応不可能な問題

## 現在のツール制限事項

### 1. 文字列・コメント内の括弧処理 ❌
**問題**: 文字列やコメント内の括弧も変換してしまう
```fsharp
// 入力
let text = "function(test)"
// Call function(arg)

// 期待結果
let text = "function(test)"
// Call function(arg)

// 実際の結果
let text = "function test"
// Call function arg
```

**理由**: 正確な文字列・コメント解析はF#パーサーが必要で、正規表現では限界
**回避策**: 手動で文字列・コメント内の変換を元に戻すか、ツール適用前に一時的に文字列を変更

### 2. 複雑な型キャスト式の括弧配置 ❌
**問題**: `int(value + offset)` → `int value + offset` （期待: `int (value + offset)`）
```fsharp
// 入力
let length = int(value + offset)

// 期待結果
let length = int (value + offset)

// 実際の結果
let length = int value + offset
```

**理由**: 式の解析順序が正規表現では完全に把握できない
**回避策**: Conservative レベルでは適用しない、または手動で括弧を追加

### 3. メソッドチェーン優先順位問題 ❌
**問題**: `Directory.GetFiles(dir).Length` の正確な括弧配置
```fsharp
// 入力
let length = Directory.GetFiles(dir).Length

// 期待結果
let length = (Directory.GetFiles dir).Length

// 実際の結果
let length = Directory.GetFiles dir.Length
```

**理由**: メソッドチェーンのパターンマッチングが不完全
**回避策**: このパターンは手動修正が必要

### 4. プロパティアクセスの括弧除去 ❌
**問題**: `list.Length()` → `list.Length` 変換が未実装
```fsharp
// 入力
let count = list.Length()

// 期待結果
let count = list.Length

// 実際の結果
let count = list.Length()
```

**理由**: プロパティとメソッドの区別がF#静的解析なしでは困難
**回避策**: 手動でプロパティアクセスの括弧を除去

### 5. ネストした関数呼び出し ❌
**問題**: 内側の関数呼び出しのみ変換される
```fsharp
// 入力
let result = outer(inner(value))

// 期待結果
let result = outer (inner value)

// 実際の結果
let result = outer(inner value)
```

**理由**: ネストしたパターンの処理順序制御が複雑
**回避策**: 複数回ツールを実行するか手動で修正

## 対応済み機能

### ✅ IDisposableコンストラクタ保護
```fsharp
let frame = new FrameView(title)  // 保護される
```

### ✅ 静的メソッド呼び出し
```fsharp
Directory.Exists(path) → Directory.Exists path
```

### ✅ 単純な型キャスト
```fsharp
float(x) → float x
```

### ✅ 複数引数の保護
```fsharp
func(arg1, arg2) // 変更されない
```

## 推奨使用方法

1. **Conservative レベル使用**: 最も安全な変換のみ
2. **ドライラン必須**: 変更内容を事前確認
3. **段階的適用**: 小さなファイルから開始
4. **手動確認**: 変換後のコードレビュー必須
5. **テスト実行**: 変換後の動作確認

## 今後の改善可能性

- **F# Compiler Service統合**: より正確な構文解析
- **AST解析**: 抽象構文木を使った正確な変換
- **IDE統合**: F# Language Serverとの連携

現在のツールは正規表現ベースの簡易版として、安全な変換のみに特化することを推奨します。