module FCode.TUIInternalAPI

open System
open FCode.Logger
open FCode.FCodeError

// ===============================================
// Issue #94: TUI内部API設計仕様（簡素化実装）
// ===============================================

/// Issue #94で定義されたエージェントメッセージタイプ
type AgentMessageType =
    | TaskRequest // タスク要求
    | TaskResponse // タスク応答
    | StatusUpdate // 状態更新
    | CollaborationRequest // 協調要求
    | ResourceShare // リソース共有

/// Issue #94: エージェント間通信API
type AgentCommunication =
    { SenderId: string // 送信者ID
      ReceiverId: string option // 受信者ID (None = ブロードキャスト)
      MessageType: AgentMessageType // メッセージタイプ
      Payload: obj // ペイロード
      Timestamp: DateTime } // タイムスタンプ

/// TUI内部ペイン状態定義
type TUIPaneState =
    | Active // アクティブ状態
    | Busy // 処理中
    | Waiting // 待機中
    | ErrorState of string // エラー状態
    | Offline // オフライン

/// 競合解決戦略
type ConflictStrategy =
    | LastWriteWins // 最後の書き込み優先
    | FirstWriteWins // 最初の書き込み優先
    | MergeChanges // 変更マージ
    | ManualResolve // 手動解決

/// Issue #94: ペイン間状態同期API
type PaneStateSync =
    { PaneId: string // ペインID
      State: TUIPaneState // ペイン状態
      LastModified: DateTime // 最終更新時刻
      ConflictResolution: ConflictStrategy } // 競合解決戦略

/// プロセス管理インターフェース（簡素化）
type IProcessManager =
    abstract member StartProcess: string -> Async<Result<string, FCodeError>>
    abstract member StopProcess: string -> Async<Result<unit, FCodeError>>
    abstract member GetProcessStatus: string -> Async<Result<string, FCodeError>>

/// 入出力キャプチャインターフェース（簡素化）
type IInputOutputCapture =
    abstract member CaptureStdout: string -> Async<Result<string, FCodeError>>
    abstract member CaptureStderr: string -> Async<Result<string, FCodeError>>
    abstract member SendInput: string * string -> Async<Result<unit, FCodeError>>

/// セッション管理インターフェース（簡素化）
type ISessionManager =
    abstract member CreateSession: string -> Async<Result<string, FCodeError>>
    abstract member GetSession: string -> Async<Result<obj, FCodeError>>
    abstract member DestroySession: string -> Async<Result<unit, FCodeError>>

/// コマンドディスパッチャーインターフェース（簡素化）
type ICommandDispatcher =
    abstract member DispatchCommand: string * string -> Async<Result<string, FCodeError>>
    abstract member RegisterCommand: string * (string -> Async<Result<string, FCodeError>>) -> unit
    abstract member UnregisterCommand: string -> unit

/// Issue #94: Claude Code プロセス統合API
type ClaudeCodeIntegration =
    { ProcessManager: IProcessManager // プロセス管理
      IOCapture: IInputOutputCapture // I/Oキャプチャ
      SessionManager: ISessionManager // セッション管理
      CommandDispatcher: ICommandDispatcher } // コマンドディスパッチャー

// ===============================================
// TUI内部API実装（簡素化）
// ===============================================

/// TUI内部エージェント通信マネージャー（簡素化）
type TUIAgentCommunicationManager() =
    let mutable subscribers = Map.empty<string, AgentCommunication -> Async<unit>>

    /// エージェント登録
    member _.RegisterAgent(agentId: string, handler: AgentCommunication -> Async<unit>) =
        subscribers <- subscribers.Add(agentId, handler)
        logInfo "TUIAgentComm" $"Agent registered: {agentId}"

    /// エージェント登録解除
    member _.UnregisterAgent(agentId: string) =
        subscribers <- subscribers.Remove(agentId)
        logInfo "TUIAgentComm" $"Agent unregistered: {agentId}"

    /// メッセージ送信
    member _.SendMessage(message: AgentCommunication) : Async<Result<unit, FCodeError>> =
        async {
            try
                let receiverText =
                    match message.ReceiverId with
                    | Some id -> id
                    | None -> "broadcast"

                logDebug "TUIAgentComm" $"Message queued: {message.SenderId} -> {receiverText}"

                // メッセージ配信
                match message.ReceiverId with
                | Some receiverId ->
                    // ユニキャスト
                    match subscribers.TryFind(receiverId) with
                    | Some handler ->
                        do! handler (message)
                        logDebug "TUIAgentComm" $"Message delivered to {receiverId}"
                    | None -> logWarning "TUIAgentComm" $"Receiver not found: {receiverId}"
                | None ->
                    // ブロードキャスト
                    for handler in subscribers.Values do
                        do! handler (message)

                    logInfo "TUIAgentComm" $"Broadcast message delivered to {subscribers.Count} agents"

                return Ok()
            with ex ->
                logException "TUIAgentComm" $"Failed to send message from {message.SenderId}" ex
                return Result.Error(SystemError($"メッセージ送信失敗: {ex.Message}"))
        }

/// TUI内部ペイン状態同期マネージャー（簡素化）
type TUIPaneStateSyncManager() =
    let mutable paneStates = Map.empty<string, PaneStateSync>

    /// ペイン状態更新
    member _.UpdatePaneState
        (paneId: string, newState: TUIPaneState, conflictStrategy: ConflictStrategy)
        : Async<Result<unit, FCodeError>> =
        async {
            try
                let now = DateTime.Now

                let stateSync =
                    { PaneId = paneId
                      State = newState
                      LastModified = now
                      ConflictResolution = conflictStrategy }

                paneStates <- paneStates.Add(paneId, stateSync)
                logInfo "TUIPaneSync" $"Pane state updated: {paneId} -> {newState}"
                return Ok()

            with ex ->
                logException "TUIPaneSync" $"Failed to update pane state: {paneId}" ex
                return Result.Error(SystemError($"ペイン状態更新失敗: {ex.Message}"))
        }

    /// ペイン状態取得
    member _.GetPaneState(paneId: string) = paneStates.TryFind(paneId)

    /// 全ペイン状態取得
    member _.GetAllPaneStates() = paneStates.Values |> Seq.toList

/// 簡易プロセス管理実装
type SimpleProcessManager() =
    interface IProcessManager with
        member _.StartProcess(command: string) =
            async {
                try
                    let processId = Guid.NewGuid().ToString("N")[..7]
                    logInfo "SimpleProcessMgr" $"Process started: {processId} ({command})"
                    return Ok(processId)
                with ex ->
                    logException "SimpleProcessMgr" $"Failed to start process: {command}" ex
                    return Result.Error(SystemError($"プロセス開始失敗: {ex.Message}"))
            }

        member _.StopProcess(processId: string) =
            async {
                try
                    logInfo "SimpleProcessMgr" $"Process stopped: {processId}"
                    return Ok()
                with ex ->
                    logException "SimpleProcessMgr" $"Failed to stop process: {processId}" ex
                    return Result.Error(SystemError($"プロセス停止失敗: {ex.Message}"))
            }

        member _.GetProcessStatus(processId: string) =
            async {
                try
                    return Ok("running")
                with ex ->
                    logException "SimpleProcessMgr" $"Failed to get process status: {processId}" ex
                    return Result.Error(SystemError($"プロセス状態取得失敗: {ex.Message}"))
            }

/// 簡易I/Oキャプチャ実装
type SimpleInputOutputCapture() =
    interface IInputOutputCapture with
        member _.CaptureStdout(processId: string) =
            async {
                try
                    return Ok($"stdout output for {processId}")
                with ex ->
                    return Result.Error(SystemError($"stdout capture failed: {ex.Message}"))
            }

        member _.CaptureStderr(processId: string) =
            async {
                try
                    return Ok($"stderr output for {processId}")
                with ex ->
                    return Result.Error(SystemError($"stderr capture failed: {ex.Message}"))
            }

        member _.SendInput(processId, input) =
            async {
                try
                    logDebug "SimpleIOCapture" $"Input sent to {processId}: {input}"
                    return Ok()
                with ex ->
                    return Result.Error(SystemError($"input send failed: {ex.Message}"))
            }

/// 簡易セッション管理実装
type SimpleSessionManager() =
    let mutable sessions = Map.empty<string, obj>

    interface ISessionManager with
        member _.CreateSession(sessionType: string) =
            async {
                try
                    let sessionId = Guid.NewGuid().ToString("N")[..7]
                    let sessionData = box $"Session data for {sessionType}"
                    sessions <- sessions.Add(sessionId, sessionData)
                    logInfo "SimpleSessionMgr" $"Session created: {sessionId} ({sessionType})"
                    return Ok(sessionId)
                with ex ->
                    return Result.Error(SystemError($"セッション作成失敗: {ex.Message}"))
            }

        member _.GetSession(sessionId: string) =
            async {
                try
                    match sessions.TryFind(sessionId) with
                    | Some sessionData -> return Ok(sessionData)
                    | None -> return Result.Error(SystemError($"セッションが見つかりません: {sessionId}"))
                with ex ->
                    return Result.Error(SystemError($"セッション取得失敗: {ex.Message}"))
            }

        member _.DestroySession(sessionId: string) =
            async {
                try
                    match sessions.TryFind(sessionId) with
                    | Some _ ->
                        sessions <- sessions.Remove(sessionId)
                        logInfo "SimpleSessionMgr" $"Session destroyed: {sessionId}"
                        return Ok()
                    | None -> return Result.Error(SystemError($"セッションが見つかりません: {sessionId}"))
                with ex ->
                    return Result.Error(SystemError($"セッション削除失敗: {ex.Message}"))
            }

/// 簡易コマンドディスパッチャー実装 - スレッドセーフ
type SimpleCommandDispatcher() =
    let mutable commands =
        Map.empty<string, string -> Async<Result<string, FCodeError>>>

    let commandsLock = obj ()

    interface ICommandDispatcher with
        member _.DispatchCommand(command, args) =
            async {
                try
                    let handler = lock commandsLock (fun () -> commands.TryFind(command))

                    match handler with
                    | Some handlerFunc ->
                        logDebug "SimpleCommandDispatcher" $"Dispatching command: {command} {args}"
                        return! handlerFunc (args)
                    | None -> return Result.Error(SystemError($"コマンドが見つかりません: {command}"))
                with ex ->
                    return Result.Error(SystemError($"コマンド実行失敗: {ex.Message}"))
            }

        member _.RegisterCommand(command, handler) =
            lock commandsLock (fun () -> commands <- commands.Add(command, handler))
            logInfo "SimpleCommandDispatcher" $"Command registered: {command}"

        member _.UnregisterCommand(command: string) =
            lock commandsLock (fun () -> commands <- commands.Remove(command))
            logInfo "SimpleCommandDispatcher" $"Command unregistered: {command}"

// ===============================================
// TUI内部API統合ファサード（簡素化）
// ===============================================

/// TUI内部API統合管理
type TUIInternalAPIManager() =
    let agentCommManager = TUIAgentCommunicationManager()
    let paneSyncManager = TUIPaneStateSyncManager()
    let processManager = SimpleProcessManager() :> IProcessManager
    let ioCapture = SimpleInputOutputCapture() :> IInputOutputCapture
    let sessionManager = SimpleSessionManager() :> ISessionManager
    let commandDispatcher = SimpleCommandDispatcher() :> ICommandDispatcher

    /// Claude Code統合インスタンス
    member _.ClaudeCodeIntegration =
        { ProcessManager = processManager
          IOCapture = ioCapture
          SessionManager = sessionManager
          CommandDispatcher = commandDispatcher }

    /// エージェント通信マネージャー
    member _.AgentCommunication = agentCommManager

    /// ペイン状態同期マネージャー
    member _.PaneStateSync = paneSyncManager

    /// 初期化
    member this.Initialize() : Async<Result<unit, FCodeError>> =
        async {
            try
                // 基本コマンド登録
                this.ClaudeCodeIntegration.CommandDispatcher.RegisterCommand(
                    ("echo", fun args -> async { return Ok($"echo: {args}") })
                )

                this.ClaudeCodeIntegration.CommandDispatcher.RegisterCommand(
                    ("status", fun _ -> async { return Ok("TUI Internal API is running") })
                )

                logInfo "TUIInternalAPI" "TUI Internal API Manager initialized successfully"
                return Ok()

            with ex ->
                logException "TUIInternalAPI" "Failed to initialize TUI Internal API Manager" ex
                return Result.Error(SystemError($"TUI内部API初期化失敗: {ex.Message}"))
        }

/// TUI内部APIファクトリー - 依存性注入対応
module TUIInternalAPIFactory =
    /// 新しいTUIInternalAPIManagerインスタンスを作成
    let createInstance () = TUIInternalAPIManager()

    /// テスト用のデフォルトインスタンス
    let mutable private defaultInstance: TUIInternalAPIManager option = None

    /// デフォルトインスタンスを設定
    let setDefaultInstance (instance: TUIInternalAPIManager) = defaultInstance <- Some instance

    /// デフォルトインスタンスを取得（lazy初期化）
    let getDefaultInstance () =
        match defaultInstance with
        | Some instance -> instance
        | None ->
            let newInstance = createInstance ()
            defaultInstance <- Some newInstance
            newInstance
