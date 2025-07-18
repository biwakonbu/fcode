module FCode.ClaudeCodeIOTrigger

open System
open System.Threading.Tasks
open FCode.Logger
open FCode.ClaudeCodeIOIntegration
open FCode.FCodeError

/// Claude Code実行のトリガー機能
/// PO指示やエージェント要求からClaude Code実行を開始
type ClaudeCodeIOTrigger(ioManager: ClaudeCodeIOIntegrationManager) =

    /// PO指示からClaude Code実行を開始
    member this.StartFromPOInstruction(instruction: string, workingDir: string) : Task<Result<unit, FCodeError>> =
        task {
            try
                logInfo "ClaudeCodeIOTrigger" (sprintf "PO指示からClaude Code実行開始: %s" instruction)

                // PO指示をClaude Codeコマンドに変換
                let sessionId = Guid.NewGuid().ToString("N").Substring(0, 8)
                let claudeCommand = "claude-code"
                let args = [| instruction |]

                // Claude Code実行開始
                let! result = ioManager.StartClaudeCodeExecution(sessionId, claudeCommand, args, workingDir)

                match result with
                | Result.Ok() ->
                    logInfo "ClaudeCodeIOTrigger" (sprintf "PO指示からClaude Code実行開始成功: sessionId=%s" sessionId)
                    return Result.Ok()
                | Result.Error error ->
                    logError "ClaudeCodeIOTrigger" (sprintf "PO指示からClaude Code実行開始失敗: %A" error)
                    return Result.Error error

            with ex ->
                logError "ClaudeCodeIOTrigger" (sprintf "PO指示からClaude Code実行開始例外: %s" ex.Message)
                return Result.Error(SystemError ex.Message)
        }

    /// エージェント要求からClaude Code実行を開始
    member this.StartFromAgentRequest
        (agentId: string, request: string, workingDir: string)
        : Task<Result<unit, FCodeError>> =
        task {
            try
                logInfo
                    "ClaudeCodeIOTrigger"
                    (sprintf "エージェント要求からClaude Code実行開始: agent=%s, request=%s" agentId request)

                // エージェント要求をClaude Codeコマンドに変換
                let sessionId =
                    sprintf "%s_%s" agentId (Guid.NewGuid().ToString("N").Substring(0, 8))

                let claudeCommand = "claude-code"
                let args = [| sprintf "[%sからの要求] %s" agentId request |]

                // Claude Code実行開始
                let! result = ioManager.StartClaudeCodeExecution(sessionId, claudeCommand, args, workingDir)

                match result with
                | Result.Ok() ->
                    logInfo "ClaudeCodeIOTrigger" (sprintf "エージェント要求からClaude Code実行開始成功: sessionId=%s" sessionId)
                    return Result.Ok()
                | Result.Error error ->
                    logError "ClaudeCodeIOTrigger" (sprintf "エージェント要求からClaude Code実行開始失敗: %A" error)
                    return Result.Error error

            with ex ->
                logError "ClaudeCodeIOTrigger" (sprintf "エージェント要求からClaude Code実行開始例外: %s" ex.Message)
                return Result.Error(SystemError ex.Message)
        }

    /// タスクからClaude Code実行を開始
    member this.StartFromTask
        (taskId: string, taskTitle: string, taskDescription: string, workingDir: string)
        : Task<Result<unit, FCodeError>> =
        task {
            try
                logInfo "ClaudeCodeIOTrigger" (sprintf "タスクからClaude Code実行開始: taskId=%s, title=%s" taskId taskTitle)

                // タスク情報をClaude Codeコマンドに変換
                let sessionId = sprintf "task_%s" taskId
                let claudeCommand = "claude-code"
                let args = [| sprintf "[タスク: %s] %s" taskTitle taskDescription |]

                // Claude Code実行開始
                let! result = ioManager.StartClaudeCodeExecution(sessionId, claudeCommand, args, workingDir)

                match result with
                | Result.Ok() ->
                    logInfo "ClaudeCodeIOTrigger" (sprintf "タスクからClaude Code実行開始成功: sessionId=%s" sessionId)
                    return Result.Ok()
                | Result.Error error ->
                    logError "ClaudeCodeIOTrigger" (sprintf "タスクからClaude Code実行開始失敗: %A" error)
                    return Result.Error error

            with ex ->
                logError "ClaudeCodeIOTrigger" (sprintf "タスクからClaude Code実行開始例外: %s" ex.Message)
                return Result.Error(SystemError ex.Message)
        }

    /// 現在のClaude Code実行状態取得
    member this.GetCurrentState() : ClaudeCodeIOState = ioManager.GetCurrentSessionState()

    /// 現在のClaude Code実行停止
    member this.Stop() : Task<Result<unit, FCodeError>> = ioManager.StopClaudeCodeExecution()

    /// Claude Code実行情報取得
    member this.GetSessionInfo() : string = ioManager.GetSessionInfo()

    /// アクティブ状態確認
    member this.IsActive() : bool = ioManager.IsActive()
