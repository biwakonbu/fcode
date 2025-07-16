module FCode.ClaudeCodeIOIntegration

open System
open System.IO
open System.Threading.Tasks
open System.Text
open Terminal.Gui
open FCode
open FCode.Logger
open FCode.ClaudeCodeProcess
open FCode.FCodeError

/// Claude Code I/O統合状態
type ClaudeCodeIOState =
    | Idle // 待機中
    | Starting // 開始中
    | Running // 実行中
    | Completed // 完了
    | Failed // 失敗
    | Terminated // 強制終了

/// Claude Code I/O統合セッション情報
type ClaudeCodeIOSession =
    { SessionId: string
      Command: string
      Args: string[]
      WorkingDirectory: string
      State: ClaudeCodeIOState
      SessionBridge: SessionBridge
      StartTime: DateTime option
      EndTime: DateTime option
      LastOutput: string option
      mutable OutputBuffer: StringBuilder }

/// Claude Code I/O統合マネージャー
/// dev1ペインでClaude Code実行結果をリアルタイム表示
type ClaudeCodeIOIntegrationManager(dev1Pane: TextView) =
    let mutable currentSession: ClaudeCodeIOSession option = None
    let mutable isActive = false
    let sessionBridge = new SessionBridge(dev1Pane)

    /// Claude Code実行開始
    member this.StartClaudeCodeExecution
        (sessionId: string, claudeCommand: string, args: string[], workingDir: string)
        : Task<Result<unit, FCodeError>> =
        task {
            try
                if isActive then
                    logWarning "ClaudeCodeIOIntegration" "既にアクティブなClaude Codeセッションが存在します"

                    return
                        Result.Error(
                            ProcessError
                                { Component = "ClaudeCodeIOIntegration"
                                  Operation = "StartClaudeCodeExecution"
                                  Message = "既にアクティブなセッションが存在します"
                                  Recoverable = true
                                  ProcessId = None }
                        )
                else
                    logInfo
                        "ClaudeCodeIOIntegration"
                        (sprintf "Claude Code実行開始: sessionId=%s, command=%s" sessionId claudeCommand)

                    // セッション情報初期化
                    let session =
                        { SessionId = sessionId
                          Command = claudeCommand
                          Args = args
                          WorkingDirectory = workingDir
                          State = Starting
                          SessionBridge = sessionBridge
                          StartTime = Some DateTime.UtcNow
                          EndTime = None
                          LastOutput = None
                          OutputBuffer = StringBuilder() }

                    currentSession <- Some session
                    isActive <- true

                    // dev1ペインに開始メッセージ表示
                    Application.MainLoop.Invoke(fun _ ->
                        let timeStr = DateTime.Now.ToString("HH:mm:ss")

                        dev1Pane.Text <-
                            NStack.ustring.Make(sprintf "[%s] Claude Code実行開始: %s\n" timeStr claudeCommand))

                    // SessionBridgeを使用してClaude Code実行
                    let! bridgeResult = sessionBridge.StartSession(sessionId, claudeCommand, args, workingDir)

                    match bridgeResult with
                    | Result.Ok() ->
                        logInfo "ClaudeCodeIOIntegration" (sprintf "Claude Code実行開始成功: sessionId=%s" sessionId)

                        // 出力監視開始
                        let! _ = Task.Run(fun () -> this.MonitorOutput(session) :> Task)

                        return Result.Ok()
                    | Result.Error error ->
                        logError "ClaudeCodeIOIntegration" (sprintf "Claude Code実行開始失敗: %s" error)
                        isActive <- false
                        currentSession <- None

                        return
                            Result.Error(
                                ProcessError
                                    { Component = "ClaudeCodeIOIntegration"
                                      Operation = "StartClaudeCodeExecution"
                                      Message = error
                                      Recoverable = true
                                      ProcessId = None }
                            )

            with ex ->
                logError "ClaudeCodeIOIntegration" (sprintf "Claude Code実行開始例外: %s" ex.Message)
                isActive <- false
                currentSession <- None
                return Result.Error(SystemError ex.Message)
        }

    /// 出力監視・リアルタイム表示
    member private this.MonitorOutput(session: ClaudeCodeIOSession) : Task<unit> =
        task {
            try
                logInfo "ClaudeCodeIOIntegration" (sprintf "出力監視開始: sessionId=%s" session.SessionId)

                // 出力監視ループ（SessionBridgeが内部的に処理）
                // リアルタイム表示はSessionBridgeが担当
                let mutable monitoring = true
                let mutable lastUpdateTime = DateTime.UtcNow

                while monitoring && isActive do
                    // 定期的な状態確認
                    do! Task.Delay(1000)

                    // タイムアウト処理（30分）
                    if DateTime.UtcNow - lastUpdateTime > TimeSpan.FromMinutes(30.0) then
                        logWarning
                            "ClaudeCodeIOIntegration"
                            (sprintf "Claude Code実行タイムアウト: sessionId=%s" session.SessionId)

                        monitoring <- false

                        Application.MainLoop.Invoke(fun _ ->
                            let timeStr = DateTime.Now.ToString("HH:mm:ss")

                            dev1Pane.Text <-
                                NStack.ustring.Make(
                                    dev1Pane.Text.ToString() + sprintf "\n[%s] タイムアウト: 30分経過\n" timeStr
                                ))

                    // セッション状態確認
                    if not isActive then
                        monitoring <- false

                logInfo "ClaudeCodeIOIntegration" (sprintf "出力監視完了: sessionId=%s" session.SessionId)

            with ex ->
                logError "ClaudeCodeIOIntegration" (sprintf "出力監視エラー: %s" ex.Message)

                Application.MainLoop.Invoke(fun _ ->
                    let timeStr = DateTime.Now.ToString("HH:mm:ss")

                    dev1Pane.Text <-
                        NStack.ustring.Make(dev1Pane.Text.ToString() + sprintf "\n[%s] エラー: %s\n" timeStr ex.Message))
        }

    /// Claude Code実行停止
    member this.StopClaudeCodeExecution() : Task<Result<unit, FCodeError>> =
        task {
            try
                if not isActive then
                    return Result.Ok()
                else
                    logInfo "ClaudeCodeIOIntegration" "Claude Code実行停止開始"

                    // SessionBridge経由でセッション停止
                    sessionBridge.StopSession()
                    let stopResult = Result.Ok() // 実際の停止処理を呼び出し

                    match stopResult with
                    | Result.Ok() ->
                        logInfo "ClaudeCodeIOIntegration" "Claude Code実行停止成功"

                        // 状態リセット
                        isActive <- false
                        currentSession <- None

                        // dev1ペインに停止メッセージ表示
                        Application.MainLoop.Invoke(fun _ ->
                            let timeStr = DateTime.Now.ToString("HH:mm:ss")

                            dev1Pane.Text <-
                                NStack.ustring.Make(
                                    dev1Pane.Text.ToString() + sprintf "\n[%s] Claude Code実行停止\n" timeStr
                                ))

                        return Result.Ok()
                    | Result.Error error ->
                        logError "ClaudeCodeIOIntegration" (sprintf "Claude Code実行停止失敗: %s" error)

                        return
                            Result.Error(
                                ProcessError
                                    { Component = "ClaudeCodeIOIntegration"
                                      Operation = "StartClaudeCodeExecution"
                                      Message = error
                                      Recoverable = true
                                      ProcessId = None }
                            )

            with ex ->
                logError "ClaudeCodeIOIntegration" (sprintf "Claude Code実行停止例外: %s" ex.Message)
                return Result.Error(SystemError ex.Message)
        }

    /// 現在のセッション状態取得
    member this.GetCurrentSessionState() : ClaudeCodeIOState =
        match currentSession with
        | Some session -> session.State
        | None -> Idle

    /// 現在のセッション情報取得
    member this.GetCurrentSession() : ClaudeCodeIOSession option = currentSession

    /// アクティブ状態確認
    member this.IsActive() : bool = isActive

    /// セッション詳細情報取得
    member this.GetSessionInfo() : string =
        match currentSession with
        | Some session ->
            let stateStr =
                match session.State with
                | Idle -> "待機中"
                | Starting -> "開始中"
                | Running -> "実行中"
                | Completed -> "完了"
                | Failed -> "失敗"
                | Terminated -> "強制終了"

            let durationStr =
                match session.StartTime with
                | Some startTime ->
                    let duration = DateTime.UtcNow - startTime
                    sprintf "実行時間: %d分%d秒" duration.Minutes duration.Seconds
                | None -> "実行時間: 不明"

            sprintf "セッション: %s\n状態: %s\nコマンド: %s\n%s" session.SessionId stateStr session.Command durationStr
        | None -> "アクティブなセッションがありません"

    /// リソース解放
    interface IDisposable with
        member this.Dispose() =
            if isActive then
                try
                    // デッドロック回避のためTaskで実行
                    Task
                        .Run(fun () -> this.StopClaudeCodeExecution() |> Async.AwaitTask |> Async.RunSynchronously)
                        .Wait(TimeSpan.FromSeconds(5.0))
                    |> ignore
                with _ ->
                    ()

            // SessionBridgeのリソース解放
            try
                (sessionBridge :> IDisposable).Dispose()
            with _ ->
                ()
