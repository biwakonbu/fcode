module FCode.FCodeError

open System

/// エラーメッセージの二層構造
type ErrorMessage =
    { UserMessage: string // ユーザー向け（日本語、分かりやすい）
      TechnicalDetails: string // 開発者向け（英語、詳細）
      RecoveryHint: string option } // 復旧方法の提案

/// プロセス操作エラー詳細
type ProcessErrorDetails =
    { Component: string
      Operation: string
      Message: string
      Recoverable: bool
      ProcessId: int option }

/// データベース操作エラー詳細
type DatabaseErrorDetails =
    { DatabasePath: string
      Operation: string
      SqlState: string option
      Message: string
      Recoverable: bool }

/// UI操作エラー詳細
type UIErrorDetails =
    { Component: string
      Operation: string
      Message: string
      RecoveryAction: string option }

/// 設定エラー詳細
type ConfigErrorDetails =
    { ConfigFile: string
      Section: string option
      Key: string option
      Message: string
      Recoverable: bool }

/// 統一エラー型（全コンポーネント共通）
type FCodeError =
    | ProcessError of ProcessErrorDetails
    | DatabaseError of DatabaseErrorDetails
    | UIError of UIErrorDetails
    | ConfigurationError of ConfigErrorDetails
    | SystemError of string
    | ValidationError of string

    /// ユーザー向けメッセージ取得
    member this.ToUserMessage() =
        match this with
        | ProcessError details ->
            if details.Recoverable then
                { UserMessage = $"プロセス操作でエラーが発生しました。再試行してください。"
                  TechnicalDetails =
                    $"Component: {details.Component}, Operation: {details.Operation}, Error: {details.Message}"
                  RecoveryHint = Some "しばらく待ってから再実行してください" }
            else
                { UserMessage = $"プロセス操作が失敗しました。設定を確認してください。"
                  TechnicalDetails =
                    $"Component: {details.Component}, Operation: {details.Operation}, Error: {details.Message}"
                  RecoveryHint = Some "Claude CLIのインストール状況を確認してください" }

        | DatabaseError details ->
            { UserMessage = $"データベース操作でエラーが発生しました。"
              TechnicalDetails =
                $"Database: {details.DatabasePath}, Operation: {details.Operation}, Error: {details.Message}"
              RecoveryHint =
                if details.Recoverable then
                    Some "データベースファイルの権限を確認してください"
                else
                    Some "アプリケーションの再起動が必要です" }

        | UIError details ->
            { UserMessage = $"画面表示でエラーが発生しました。"
              TechnicalDetails =
                $"Component: {details.Component}, Operation: {details.Operation}, Error: {details.Message}"
              RecoveryHint = details.RecoveryAction }

        | ConfigurationError details ->
            { UserMessage = $"設定ファイルの読み込みに失敗しました。"
              TechnicalDetails = $"Config: {details.ConfigFile}, Error: {details.Message}"
              RecoveryHint =
                if details.Recoverable then
                    Some "設定ファイルの形式を確認してください"
                else
                    Some "デフォルト設定で続行します" }


        | SystemError message ->
            { UserMessage = $"システムエラーが発生しました。"
              TechnicalDetails = message
              RecoveryHint = Some "アプリケーションの再起動を試してください" }

        | ValidationError message ->
            { UserMessage = $"入力値が不正です。"
              TechnicalDetails = message
              RecoveryHint = Some "入力内容を確認してください" }

/// エラーハンドリングユーティリティ
module ErrorHandling =

    /// 操作結果のログ出力（統一パターン）
    let handleOperationWithLogging
        (logger: string -> string -> unit)
        (errorLogger: string -> string -> unit)
        (debugLogger: string -> string -> unit)
        (category: string)
        (operation: string)
        (result: Result<'T, FCodeError>)
        =
        match result with
        | Ok value ->
            logger category $"{operation} completed successfully"
            Ok value
        | Error error ->
            let errorMsg = error.ToUserMessage()
            errorLogger category $"{operation} failed: {errorMsg.UserMessage}"
            debugLogger category $"{operation} technical details: {errorMsg.TechnicalDetails}"
            Error error

    /// Exception → FCodeError変換
    let fromException (category: string) (operation: string) (ex: Exception) =
        match ex with
        | :? System.IO.IOException as ioEx ->
            DatabaseError
                { DatabasePath = "unknown"
                  Operation = operation
                  SqlState = None
                  Message = ioEx.Message
                  Recoverable = true }
        | :? ArgumentException as argEx -> ValidationError argEx.Message
        | :? UnauthorizedAccessException as authEx ->
            ProcessError
                { Component = category
                  Operation = operation
                  Message = authEx.Message
                  Recoverable = false
                  ProcessId = None }
        | _ -> SystemError ex.Message

    /// 非同期操作のエラーハンドリング
    let handleAsyncOperation (category: string) (operation: string) (asyncOp: Async<'T>) =
        async {
            try
                let! result = asyncOp
                return Ok result
            with ex ->
                return Error(fromException category operation ex)
        }

    /// プロセス起動専用エラー作成
    let createProcessError (comp: string) (operation: string) (message: string) (recoverable: bool) =
        ProcessError
            { Component = comp
              Operation = operation
              Message = message
              Recoverable = recoverable
              ProcessId = None }

    /// データベースエラー専用作成
    let createDatabaseError (dbPath: string) (operation: string) (message: string) (recoverable: bool) =
        DatabaseError
            { DatabasePath = dbPath
              Operation = operation
              SqlState = None
              Message = message
              Recoverable = recoverable }

    /// UIエラー専用作成
    let createUIError (comp: string) (operation: string) (message: string) (recoveryAction: string option) =
        UIError
            { Component = comp
              Operation = operation
              Message = message
              RecoveryAction = recoveryAction }

    /// 設定エラー専用作成
    let createConfigError
        (configFile: string)
        (section: string option)
        (key: string option)
        (message: string)
        (recoverable: bool)
        =
        ConfigurationError
            { ConfigFile = configFile
              Section = section
              Key = key
              Message = message
              Recoverable = recoverable }
