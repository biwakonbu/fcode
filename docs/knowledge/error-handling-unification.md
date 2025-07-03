# エラーハンドリング統一化のベストプラクティス

## 概要

大規模F#プロジェクトにおけるエラーハンドリングの統一化戦略と、実装時の技術的な課題への対処法をまとめた文書。

## 統一化アーキテクチャ

### FCodeError統一型設計

```fsharp
/// 統一エラー型（全コンポーネント共通）
type FCodeError =
    | ProcessError of ProcessErrorDetails
    | DatabaseError of DatabaseErrorDetails
    | UIError of UIErrorDetails
    | ConfigurationError of ConfigErrorDetails
    | SystemError of string
    | ValidationError of string

/// エラーメッセージの二層構造
type ErrorMessage =
    { UserMessage: string        // ユーザー向け（日本語、分かりやすい）
      TechnicalDetails: string   // 開発者向け（英語、詳細）
      RecoveryHint: string option } // 復旧方法の提案
```

### Railway Oriented Programming採用

```fsharp
/// 操作結果のログ出力（統一パターン）
let handleOperationWithLogging
    (logger: string -> string -> unit)
    (errorLogger: string -> string -> unit)
    (operation: string)
    (result: Result<'T, FCodeError>) =
    match result with
    | Ok value ->
        logger "Operation" $"{operation} completed successfully"
        Ok value
    | Error error ->
        let errorMsg = error.ToUserMessage()
        errorLogger "Operation" $"{operation} failed: {errorMsg.UserMessage}"
        Error error
```

## 段階的移行戦略

### Phase 1: failwith例外の完全除去
```fsharp
// Before: クラッシュリスク
let processData input =
    if String.IsNullOrEmpty(input) then
        failwith "Invalid input"  // 危険
    // ...

// After: 安全な戻り値
let processData input =
    if String.IsNullOrEmpty(input) then
        Error (ValidationError "Input cannot be null or empty")
    else
        // ...
        Ok result
```

### Phase 2: 大型関数の責務分離
```fsharp
// Before: 283行の巨大関数
member _.StartSession(paneId, workingDir, outputView) =
    // 283行の複雑なロジック

// After: 責務別分離
member _.StartSession(paneId, workingDir, outputView) =
    validateInput paneId workingDir
    |> Result.bind findClaudePath
    |> Result.bind createProcessInfo
    |> Result.bind startProcess
    |> Result.bind setupIOHandlers
```

### Phase 3: 型安全性の向上
```fsharp
// 型安全なヘルパー関数群
module ErrorHandling =
    let createProcessError comp operation message recoverable =
        ProcessError {
            Component = comp
            Operation = operation  
            Message = message
            Recoverable = recoverable
            ProcessId = None
        }
```

## 技術的な落とし穴と対策

### Union型コンストラクタ認識問題

#### 問題
```fsharp
// F#コンパイラが認識できない
Error (ProcessError details)  // FS0003エラー
```

#### 解決策
```fsharp
// 1. 明示的なコンストラクタ関数
let createError err = Result.Error err

// 2. 完全修飾名使用
Result<unit, FCode.FCodeError.FCodeError>

// 3. 型エイリアス使用
type OperationResult<'T> = Result<'T, FCodeError>
```

### モジュール境界の問題

#### 対策パターン
```fsharp
// ErrorHandlingモジュールで統一インターフェース提供
module ErrorHandling =
    /// Exception → FCodeError変換
    let fromException (category: string) (operation: string) (ex: Exception) =
        match ex with
        | :? IOException as ioEx -> 
            DatabaseError { /* ... */ }
        | :? ArgumentException as argEx -> 
            ValidationError argEx.Message
        | _ -> 
            SystemError ex.Message

    /// 非同期操作のエラーハンドリング
    let handleAsyncOperation category operation asyncOp =
        async {
            try
                let! result = asyncOp
                return Ok result
            with ex ->
                return Error(fromException category operation ex)
        }
```

## テスト戦略

### エラーケースの網羅的テスト
```fsharp
[<Test>]
let ``ProcessError cases should be handled correctly`` () =
    let testCases = [
        (ProcessError processDetails, "プロセス操作でエラーが発生しました")
        (DatabaseError dbDetails, "データベース操作でエラーが発生しました")
        (UIError uiDetails, "画面表示でエラーが発生しました")
    ]
    
    testCases
    |> List.iter (fun (error, expectedMsg) ->
        let message = error.ToUserMessage()
        message.UserMessage |> should contain expectedMsg)
```

### 異常系統合テスト
```fsharp
[<Test>]
let ``Database disconnection should be handled gracefully`` () =
    // データベース切断シミュレーション
    let result = TaskStorageManager.saveTask disconnectedDb task
    
    match result with
    | Error (DatabaseError details) ->
        details.Recoverable |> should equal true
        details.Message |> should contain "connection"
    | _ -> failwith "Expected DatabaseError"
```

## パフォーマンス考慮事項

### エラー処理のオーバーヘッド最小化
```fsharp
// 高頻度操作では軽量エラー型を使用
type LightweightError = string  // シンプルなエラー表現

// 低頻度操作では詳細エラー型を使用
type DetailedError = FCodeError  // 完全なエラー情報
```

### ログ出力の最適化
```fsharp
// ログレベル別の条件付き出力
let logErrorConditionally level category message =
    if logger.IsEnabled(level) then
        logger.Log(level, category, message)
```

## 運用面での考慮

### ユーザビリティの向上
- **二層メッセージ構造**: ユーザー向けと開発者向けの分離
- **復旧ヒント**: 具体的な解決方法の提示
- **日本語対応**: エンドユーザー向けメッセージの国際化

### 監視・運用サポート
- **構造化ログ**: 機械処理可能な形式での出力
- **エラー分類**: 自動集計・分析のためのカテゴリ化
- **復旧可能性フラグ**: 自動復旧システムとの連携

## 継続的改善

### メトリクス収集
- エラー発生頻度の追跡
- 復旧成功率の測定
- ユーザー体験の定量化

### フィードバックループ
- 開発者からの改善提案
- ユーザーからのエラーメッセージ評価
- 運用チームからの監視改善要求

## 関連資料

- [Railway Oriented Programming](https://fsharpforfunandprofit.com/rop/)
- [F# Error Handling Best Practices](https://docs.microsoft.com/en-us/dotnet/fsharp/language-reference/exception-handling/)
- [Structured Logging with Serilog](https://serilog.net/)