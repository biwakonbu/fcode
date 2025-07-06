module FCode.InputValidation

open System
open System.Text.RegularExpressions
open FCode.Logger

// ===============================================
// セキュリティ強化: 入力検証システム
// ===============================================

/// 入力検証結果
type ValidationResult<'T> =
    | Valid of 'T
    | Invalid of string list // エラーメッセージリスト

/// 入力検証ルール
type ValidationRule<'T> = 'T -> ValidationResult<'T>

/// セキュリティ検証レベル
type SecurityLevel =
    | Basic // 基本チェック（空文字・null）
    | Standard // 標準チェック（長さ・形式）
    | Strict // 厳格チェック（パターン・文字種制限）
    | Critical // 重要チェック（SQLインジェクション・XSS対策）

// ===============================================
// 基本検証ルール
// ===============================================

module ValidationRules =

    /// 空文字・null検証
    let notNullOrEmpty (fieldName: string) : ValidationRule<string> =
        fun input ->
            if String.IsNullOrWhiteSpace(input) then
                Invalid [ $"{fieldName}は必須項目です" ]
            else
                Valid input

    /// 長さ制限検証
    let lengthBetween (fieldName: string) (minLength: int) (maxLength: int) : ValidationRule<string> =
        fun input ->
            let length = if isNull input then 0 else input.Length

            if length < minLength then
                Invalid [ $"{fieldName}は{minLength}文字以上で入力してください" ]
            elif length > maxLength then
                Invalid [ $"{fieldName}は{maxLength}文字以下で入力してください" ]
            else
                Valid input

    /// 正規表現パターン検証
    let matchesPattern (fieldName: string) (pattern: string) (description: string) : ValidationRule<string> =
        fun input ->
            if String.IsNullOrWhiteSpace(input) then
                Valid input // 空の場合は別のルールで検証
            else
                try
                    if Regex.IsMatch(input, pattern) then
                        Valid input
                    else
                        Invalid [ $"{fieldName}は{description}の形式で入力してください" ]
                with ex ->
                    logError "InputValidation" $"正規表現エラー: {ex.Message}"
                    Invalid [ $"{fieldName}の形式が無効です" ]

    /// エージェントID検証（英数字・ハイフン・アンダースコアのみ）
    let validAgentId: ValidationRule<string> =
        matchesPattern "エージェントID" "^[a-zA-Z0-9_-]+$" "英数字、ハイフン、アンダースコアのみ"

    /// メッセージ内容検証（基本的なXSS対策）
    let safeMessageContent: ValidationRule<string> =
        fun input ->
            if String.IsNullOrWhiteSpace(input) then
                Invalid [ "メッセージ内容は必須です" ]
            else
                // 危険な文字列パターンチェック
                let dangerousPatterns =
                    [ "<script"
                      "javascript:"
                      "vbscript:"
                      "onload="
                      "onerror="
                      "onclick="
                      "eval("
                      "document.cookie"
                      "window.location" ]

                let lowerInput = input.ToLowerInvariant()

                let foundDangerous =
                    dangerousPatterns |> List.filter (fun pattern -> lowerInput.Contains(pattern))

                if foundDangerous.Length > 0 then
                    let dangerousStr = String.concat ", " foundDangerous
                    logWarning "InputValidation" $"危険なパターンを検出: {dangerousStr}"
                    Invalid [ "メッセージに不正な内容が含まれています" ]
                else
                    Valid input

    /// SQLインジェクション対策検証
    let sqlSafeString: ValidationRule<string> =
        fun input ->
            if String.IsNullOrWhiteSpace(input) then
                Valid input
            else
                let sqlPatterns =
                    [ "'"
                      "\""
                      ";"
                      "--"
                      "/*"
                      "*/"
                      "union"
                      "select"
                      "insert"
                      "update"
                      "delete"
                      "drop"
                      "exec"
                      "execute" ]

                let lowerInput = input.ToLowerInvariant()

                let foundSqlPatterns =
                    sqlPatterns |> List.filter (fun pattern -> lowerInput.Contains(pattern))

                if foundSqlPatterns.Length > 0 then
                    let sqlStr = String.concat ", " foundSqlPatterns
                    logWarning "InputValidation" $"SQL関連パターンを検出: {sqlStr}"
                    Invalid [ "入力にSQL関連の危険な文字が含まれています" ]
                else
                    Valid input

// ===============================================
// 検証コンポーザー
// ===============================================

module ValidationComposer =

    /// 複数の検証ルールを組み合わせる
    let combine (rules: ValidationRule<'T> list) : ValidationRule<'T> =
        fun input ->
            let results = rules |> List.map (fun rule -> rule input)

            let errors =
                results
                |> List.collect (function
                    | Invalid errs -> errs
                    | Valid _ -> [])

            if errors.Length > 0 then Invalid errors else Valid input

    /// 検証結果をマップする
    let map (f: 'T -> 'U) (validation: ValidationResult<'T>) : ValidationResult<'U> =
        match validation with
        | Valid value -> Valid(f value)
        | Invalid errors -> Invalid errors

    /// 検証結果をバインドする
    let bind (f: 'T -> ValidationResult<'U>) (validation: ValidationResult<'T>) : ValidationResult<'U> =
        match validation with
        | Valid value -> f value
        | Invalid errors -> Invalid errors

// ===============================================
// 型別専用検証器
// ===============================================

/// エージェントメッセージ検証器
type AgentMessageValidator() =

    /// エージェントID検証（セキュリティレベル別）
    member this.ValidateAgentId(agentId: string, level: SecurityLevel) : ValidationResult<string> =
        let rules =
            match level with
            | Basic -> [ ValidationRules.notNullOrEmpty "エージェントID" ]
            | Standard ->
                [ ValidationRules.notNullOrEmpty "エージェントID"
                  ValidationRules.lengthBetween "エージェントID" 1 50 ]
            | Strict ->
                [ ValidationRules.notNullOrEmpty "エージェントID"
                  ValidationRules.lengthBetween "エージェントID" 1 50
                  ValidationRules.validAgentId ]
            | Critical ->
                [ ValidationRules.notNullOrEmpty "エージェントID"
                  ValidationRules.lengthBetween "エージェントID" 1 50
                  ValidationRules.validAgentId
                  ValidationRules.sqlSafeString ]

        ValidationComposer.combine rules agentId

    /// メッセージ内容検証（セキュリティレベル別）
    member this.ValidateMessageContent(content: string, level: SecurityLevel) : ValidationResult<string> =
        let rules =
            match level with
            | Basic -> [ ValidationRules.notNullOrEmpty "メッセージ内容" ]
            | Standard ->
                [ ValidationRules.notNullOrEmpty "メッセージ内容"
                  ValidationRules.lengthBetween "メッセージ内容" 1 10000 ]
            | Strict ->
                [ ValidationRules.notNullOrEmpty "メッセージ内容"
                  ValidationRules.lengthBetween "メッセージ内容" 1 10000
                  ValidationRules.safeMessageContent ]
            | Critical ->
                [ ValidationRules.notNullOrEmpty "メッセージ内容"
                  ValidationRules.lengthBetween "メッセージ内容" 1 10000
                  ValidationRules.safeMessageContent
                  ValidationRules.sqlSafeString ]

        ValidationComposer.combine rules content

    /// メタデータキー検証
    member this.ValidateMetadataKey(key: string) : ValidationResult<string> =
        let rules =
            [ ValidationRules.notNullOrEmpty "メタデータキー"
              ValidationRules.lengthBetween "メタデータキー" 1 100
              ValidationRules.matchesPattern "メタデータキー" "^[a-zA-Z0-9_-]+$" "英数字、アンダースコア、ハイフンのみ" ]

        ValidationComposer.combine rules key

    /// メタデータ値検証
    member this.ValidateMetadataValue(value: string) : ValidationResult<string> =
        let rules =
            [ ValidationRules.lengthBetween "メタデータ値" 0 1000
              ValidationRules.safeMessageContent ]

        ValidationComposer.combine rules value

/// 進捗メトリクス検証器
type MetricsValidator() =

    /// メトリクス名検証
    member this.ValidateMetricName(name: string) : ValidationResult<string> =
        let rules =
            [ ValidationRules.notNullOrEmpty "メトリクス名"
              ValidationRules.lengthBetween "メトリクス名" 1 100
              ValidationRules.safeMessageContent ]

        ValidationComposer.combine rules name

    /// メトリクス値検証
    member this.ValidateMetricValue(value: float, minValue: float, maxValue: float) : ValidationResult<float> =
        fun () ->
            if Double.IsNaN(value) then
                Invalid [ "メトリクス値が無効です（NaN）" ]
            elif Double.IsInfinity(value) then
                Invalid [ "メトリクス値が無効です（無限大）" ]
            elif value < minValue then
                Invalid [ $"メトリクス値は{minValue}以上である必要があります" ]
            elif value > maxValue then
                Invalid [ $"メトリクス値は{maxValue}以下である必要があります" ]
            else
                Valid value
        |> fun validator -> validator ()

    /// 単位文字列検証
    member this.ValidateUnit(unit: string) : ValidationResult<string> =
        let rules =
            [ ValidationRules.notNullOrEmpty "単位"
              ValidationRules.lengthBetween "単位" 1 10
              ValidationRules.matchesPattern "単位" "^[a-zA-Z%]+$" "英字またはパーセント記号のみ" ]

        ValidationComposer.combine rules unit

// ===============================================
// パフォーマンス最適化検証
// ===============================================

/// パフォーマンス検証器
type PerformanceValidator() =

    /// 大量データ処理前検証
    member this.ValidateBatchSize(size: int, maxBatchSize: int) : ValidationResult<int> =
        if size <= 0 then
            Invalid [ "バッチサイズは1以上である必要があります" ]
        elif size > maxBatchSize then
            Invalid [ $"バッチサイズは{maxBatchSize}以下である必要があります（パフォーマンス制限）" ]
        else
            Valid size

    /// メモリ使用量チェック
    member this.ValidateMemoryUsage(currentMemoryMB: int64, maxMemoryMB: int64) : ValidationResult<unit> =
        if currentMemoryMB > maxMemoryMB then
            let warningMsg = $"メモリ使用量が制限を超過: {currentMemoryMB}MB > {maxMemoryMB}MB"
            logWarning "PerformanceValidator" warningMsg
            Invalid [ "メモリ使用量が制限を超過しています" ]
        else
            Valid()

    /// 処理時間制限チェック
    member this.ValidateProcessingTime(elapsedMs: int64, maxMs: int64) : ValidationResult<unit> =
        if elapsedMs > maxMs then
            let warningMsg = $"処理時間が制限を超過: {elapsedMs}ms > {maxMs}ms"
            logWarning "PerformanceValidator" warningMsg
            Invalid [ "処理時間が制限を超過しています" ]
        else
            Valid()

// ===============================================
// グローバル検証インスタンス
// ===============================================

/// グローバル検証器インスタンス
let agentMessageValidator = AgentMessageValidator()
let metricsValidator = MetricsValidator()
let performanceValidator = PerformanceValidator()

// ===============================================
// 便利関数
// ===============================================

/// 検証結果を Result<'T, string> に変換
let toResult (validation: ValidationResult<'T>) : Result<'T, string> =
    match validation with
    | Valid value -> Result.Ok value
    | Invalid errors -> Result.Error(String.concat "; " errors)

/// 検証結果からエラーメッセージを取得
let getErrorMessages (validation: ValidationResult<'T>) : string list =
    match validation with
    | Valid _ -> []
    | Invalid errors -> errors

/// 検証成功かどうかを判定
let isValid (validation: ValidationResult<'T>) : bool =
    match validation with
    | Valid _ -> true
    | Invalid _ -> false
