module FCode.Logger

open System
open System.IO
open System.Threading

type LogLevel =
    | Debug
    | Info
    | Warning
    | Error

type Logger(?sanitizerFunction: string -> string) =
    let logDir = Path.Combine(Path.GetTempPath(), "fcode-logs")

    let logFile =
        let timestamp = DateTime.Now.ToString("yyyy-MM-dd-HH-mm-ss")
        Path.Combine(logDir, "fcode-" + timestamp + ".log")

    let lockObj = obj ()

    do
        // ログディレクトリを作成
        if not (Directory.Exists(logDir)) then
            Directory.CreateDirectory(logDir) |> ignore

        // 初期化ログ
        let initTimestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff")

        let initMsg =
            "[" + initTimestamp + "] [INFO] [Logger] ログシステム初期化完了 - ログファイル: " + logFile

        File.AppendAllText(logFile, initMsg + Environment.NewLine)

    member _.LogPath = logFile

    member _.Log(level: LogLevel, category: string, message: string) =
        lock lockObj (fun () ->
            try
                let timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff")

                let levelStr =
                    match level with
                    | Debug -> "DEBUG"
                    | Info -> "INFO"
                    | Warning -> "WARN"
                    | Error -> "ERROR"

                // ログメッセージから機密情報を除去
                let sanitizedMessage =
                    match sanitizerFunction with
                    | Some sanitizer -> sanitizer message
                    | None -> message

                let logLine =
                    "[" + timestamp + "] [" + levelStr + "] [" + category + "] " + sanitizedMessage

                File.AppendAllText(logFile, logLine + Environment.NewLine)

                // 重要なエラーはコンソールにも出力
                if level = Error then
                    Console.WriteLine(logLine)
            with ex ->
                // ログ出力でエラーが発生した場合はコンソールのみに出力（機密情報除去）
                let sanitizedMessage =
                    match sanitizerFunction with
                    | Some sanitizer -> sanitizer message
                    | None -> message

                let sanitizedExceptionType = ex.GetType().Name

                Console.WriteLine(
                    "LOG ERROR: "
                    + sanitizedExceptionType
                    + " - Original: ["
                    + level.ToString()
                    + "] ["
                    + category
                    + "] "
                    + sanitizedMessage
                ))

    member this.Debug(category: string, message: string) = this.Log(Debug, category, message)
    member this.Info(category: string, message: string) = this.Log(Info, category, message)
    member this.Warning(category: string, message: string) = this.Log(Warning, category, message)
    member this.Error(category: string, message: string) = this.Log(Error, category, message)

    member this.Exception(category: string, message: string, ex: Exception option) =
        let sanitizedMessage =
            match sanitizerFunction with
            | Some sanitizer -> sanitizer message
            | None -> message

        match ex with
        | None ->
            let fullMessage = sanitizedMessage + " - Exception: Unknown exception occurred"
            this.Log(Error, category, fullMessage)
        | Some ex ->
            let sanitizedExceptionMessage =
                match sanitizerFunction with
                | Some sanitizer -> sanitizer ex.Message
                | None -> ex.Message

            let fullMessage =
                sanitizedMessage
                + " - Exception: "
                + sanitizedExceptionMessage
                + " - Type: "
                + ex.GetType().Name

            this.Log(Error, category, fullMessage)

// グローバルロガーインスタンス（セキュリティ機能付き）
let logger = Logger(SecurityUtils.sanitizeLogMessage)

// 便利な関数
let logDebug category message = logger.Debug(category, message)
let logInfo category message = logger.Info(category, message)
let logWarning category message = logger.Warning(category, message)
let logError category message = logger.Error(category, message)
let logException category message ex = logger.Exception(category, message, Some ex)
