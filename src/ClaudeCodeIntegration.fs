module FCode.ClaudeCodeIntegration

open System
open System.Threading
open System.Threading.Tasks
open Terminal.Gui
open FCode.Logger

/// Claude Code統合管理（簡易版）
type ClaudeCodeIntegrationManager() =

    let mutable outputBuffer: string list = []
    let outputLock = obj ()
    let mutable isRunning = false

    /// 出力バッファにテキストを追加
    let addToBuffer (text: string) =
        lock outputLock (fun () ->
            outputBuffer <- text :: outputBuffer

            if outputBuffer.Length > 1000 then
                outputBuffer <- outputBuffer |> List.take 1000)

    /// 出力バッファを取得
    member this.GetOutputBuffer() =
        lock outputLock (fun () -> outputBuffer |> List.rev)

    /// Claude Code CLIプロセスを起動（シミュレーション）
    member this.StartClaudeCode(workingDirectory: string) =
        try
            FCode.Logger.logInfo "ClaudeCodeIntegration" "Claude Code CLI起動シミュレーション開始"

            isRunning <- true

            // シミュレーション出力を追加
            addToBuffer "Claude Code CLI シミュレーション開始"
            addToBuffer (sprintf "作業ディレクトリ: %s" workingDirectory)
            addToBuffer "Claude Code > 準備完了"

            FCode.Logger.logInfo "ClaudeCodeIntegration" "Claude Code CLI起動シミュレーション成功"
            Result.Ok "Claude Code CLI起動完了（シミュレーション）"

        with ex ->
            FCode.Logger.logError "ClaudeCodeIntegration" (sprintf "Claude Code CLI起動エラー: %s" ex.Message)
            Result.Error(sprintf "起動エラー: %s" ex.Message)

    /// Claude Codeにコマンドを送信（シミュレーション）
    member this.SendCommand(command: string) =
        try
            if isRunning then
                FCode.Logger.logInfo
                    "ClaudeCodeIntegration"
                    (sprintf "コマンド送信シミュレーション: %s" (command.Substring(0, min 50 command.Length)))

                addToBuffer (sprintf "> %s" command)
                addToBuffer (sprintf "Claude Code > コマンド '%s' を実行中..." (command.Substring(0, min 20 command.Length)))
                addToBuffer "Claude Code > 実行完了"
                Result.Ok "コマンド送信完了（シミュレーション）"
            else
                let error = "Claude Codeプロセスが起動していません"
                FCode.Logger.logWarning "ClaudeCodeIntegration" error
                Result.Error error
        with ex ->
            FCode.Logger.logError "ClaudeCodeIntegration" (sprintf "コマンド送信エラー: %s" ex.Message)
            Result.Error(sprintf "送信エラー: %s" ex.Message)

    /// TextViewにリアルタイム出力を表示
    member this.UpdateTextView(textView: TextView) =
        try
            let buffer = this.GetOutputBuffer()
            let text = String.Join("\n", buffer)

            Application.MainLoop.Invoke(fun () ->
                try
                    textView.Text <- text
                    // 最新の出力にスクロール
                    textView.MoveEnd()
                with ex ->
                    FCode.Logger.logError "ClaudeCodeIntegration" (sprintf "TextView更新エラー: %s" ex.Message))
        with ex ->
            FCode.Logger.logError "ClaudeCodeIntegration" (sprintf "TextView更新処理エラー: %s" ex.Message)

    /// リアルタイム更新を開始
    member this.StartRealtimeUpdate(textView: TextView) =
        Task.Run(fun () ->
            try
                while isRunning do
                    this.UpdateTextView(textView)
                    Thread.Sleep(500) // 0.5秒間隔で更新
            with ex ->
                FCode.Logger.logError "ClaudeCodeIntegration" (sprintf "リアルタイム更新エラー: %s" ex.Message))
        |> ignore

    /// プロセス状態取得
    member this.GetStatus() =
        if isRunning then "実行中（シミュレーション）" else "停止中"

    /// Claude Codeプロセスを停止
    member this.StopClaudeCode() =
        try
            isRunning <- false
            addToBuffer "Claude Code CLI 停止"
            FCode.Logger.logInfo "ClaudeCodeIntegration" "Claude Code CLI停止完了"
            Result.Ok "Claude Code CLI停止完了"
        with ex ->
            FCode.Logger.logError "ClaudeCodeIntegration" (sprintf "Claude Code CLI停止エラー: %s" ex.Message)
            Result.Error(sprintf "停止エラー: %s" ex.Message)

    /// リソース解放
    member this.Dispose() =
        try
            this.StopClaudeCode() |> ignore
            lock outputLock (fun () -> outputBuffer <- [])
            FCode.Logger.logInfo "ClaudeCodeIntegration" "ClaudeCodeIntegrationManager disposed"
        with ex ->
            FCode.Logger.logError "ClaudeCodeIntegration" (sprintf "Dispose例外: %s" ex.Message)

    interface IDisposable with
        member this.Dispose() = this.Dispose()
