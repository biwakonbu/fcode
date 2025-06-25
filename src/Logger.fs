module TuiPoC.Logger

open System
open System.IO
open System.Threading

type LogLevel =
    | Debug
    | Info
    | Warning
    | Error

type Logger() =
    let logDir = Path.Combine(Path.GetTempPath(), "fcode-logs")

    let logFile =
        let timestamp = DateTime.Now.ToString("yyyy-MM-dd-HH-mm-ss")
        Path.Combine(logDir, $"fcode-{timestamp}.log")

    let lockObj = obj ()

    do
        // ログディレクトリを作成
        if not (Directory.Exists(logDir)) then
            Directory.CreateDirectory(logDir) |> ignore

        // 初期化ログ
        let initTimestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff")
        let initMsg = $"[{initTimestamp}] [INFO] [Logger] ログシステム初期化完了 - ログファイル: {logFile}"
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

                let logLine = $"[{timestamp}] [{levelStr}] [{category}] {message}"
                File.AppendAllText(logFile, logLine + Environment.NewLine)

                // 重要なエラーはコンソールにも出力
                if level = Error then
                    Console.WriteLine(logLine)
            with ex ->
                // ログ出力でエラーが発生した場合はコンソールのみに出力
                Console.WriteLine($"LOG ERROR: {ex.Message} - Original: [{level}] [{category}] {message}"))

    member this.Debug(category: string, message: string) = this.Log(Debug, category, message)
    member this.Info(category: string, message: string) = this.Log(Info, category, message)
    member this.Warning(category: string, message: string) = this.Log(Warning, category, message)
    member this.Error(category: string, message: string) = this.Log(Error, category, message)

    member this.Exception(category: string, message: string, ex: Exception) =
        let fullMessage =
            $"{message} - Exception: {ex.Message} - StackTrace: {ex.StackTrace}"

        this.Log(Error, category, fullMessage)

// グローバルロガーインスタンス
let logger = Logger()

// 便利な関数
let logDebug category message = logger.Debug(category, message)
let logInfo category message = logger.Info(category, message)
let logWarning category message = logger.Warning(category, message)
let logError category message = logger.Error(category, message)
let logException category message ex = logger.Exception(category, message, ex)
