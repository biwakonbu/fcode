namespace FCode

open System
open System.IO
open System.Threading.Tasks
open Terminal.Gui
open FCode
open FCode.Logger

/// PTY出力ストリームをTerminal.Gui TextViewにリアルタイム表示する統合ブリッジ
/// フォールバック機能により、PTY失敗時は標準Processを使用
type SessionBridge(pane: TextView) =
    let mutable ptyManager: PtyNetManager option = None
    let mutable fallbackManager: FallbackProcessManager option = None
    let mutable isActive = false
    let mutable isUsingFallback = false

    /// セッション開始: PTY作成とリアルタイム表示開始（フォールバック対応）
    member this.StartSession
        (sessionId: string, command: string, args: string[], workingDir: string)
        : Task<Result<unit, string>> =
        task {
            try
                if isActive then
                    return Result.Error "既にアクティブなセッションが存在します"
                else
                    logInfo "SessionBridge" (sprintf "セッション開始: sessionId=%s, command=%s" sessionId command)

                    // 最初にPTY接続を試行
                    let! ptyResult = this.TryStartPtySession(sessionId, command, args)

                    match ptyResult with
                    | Result.Ok() ->
                        logInfo "SessionBridge" (sprintf "PTYセッション開始成功: sessionId=%s" sessionId)
                        return Result.Ok()
                    | Result.Error ptyError ->
                        logWarning "SessionBridge" (sprintf "PTY失敗、フォールバックモードに切り替え: %s" ptyError)

                        // フォールバックプロセスを開始
                        return! this.StartFallbackSession(sessionId, command, args, workingDir)

            with ex ->
                logError "SessionBridge" (sprintf "セッション開始例外: %s" ex.Message)
                return Result.Error ex.Message
        }

    /// リアルタイム表示ループ（非同期実行）
    member private this.StartRealtimeDisplay(manager: PtyNetManager) : Task<unit> =
        task {
            try
                logInfo "SessionBridge" "リアルタイム表示ループ開始"
                let mutable lastOutput = ""

                while isActive do
                    // PTY出力バッファから新しいデータを取得
                    let currentOutput = manager.GetOutput()

                    // 新しい出力がある場合のみUI更新
                    if currentOutput <> lastOutput then
                        Application.MainLoop.Invoke(fun () ->
                            // TextView更新（メインスレッドで実行）
                            pane.Text <- NStack.ustring.Make(currentOutput)
                            pane.SetNeedsDisplay())

                        lastOutput <- currentOutput
                        logDebug "SessionBridge" (sprintf "TextView更新: %d文字" currentOutput.Length)

                    // CPU使用率制御（100ms間隔）
                    do! Task.Delay(100)

            with ex ->
                logError "SessionBridge" (sprintf "リアルタイム表示ループエラー: %s" ex.Message)
                isActive <- false
        }

    /// PTYセッション開始を試行
    member private this.TryStartPtySession
        (sessionId: string, command: string, args: string[])
        : Task<Result<unit, string>> =
        task {
            try
                // PTYマネージャーの作成
                let manager = new PtyNetManager()

                // PTYセッションの作成
                let! sessionResult = manager.CreateSession(command, args)

                match sessionResult with
                | Result.Ok session ->
                    ptyManager <- Some manager
                    isActive <- true
                    isUsingFallback <- false

                    // 出力読み取り開始
                    do! manager.StartOutputReading()

                    // リアルタイム表示ループの開始
                    let! _ = Task.Run<Task<unit>>(fun () -> this.StartRealtimeDisplay(manager))

                    return Result.Ok()

                | Result.Error errorMsg ->
                    (manager :> IDisposable).Dispose()
                    return Result.Error errorMsg

            with ex ->
                return Result.Error ex.Message
        }

    /// フォールバックセッション開始
    member private this.StartFallbackSession
        (sessionId: string, command: string, args: string[], workingDir: string)
        : Task<Result<unit, string>> =
        task {
            try
                this.ShowFallbackNotification()

                let manager = new FallbackProcessManager()

                match manager.StartProcess(sessionId, command, args, workingDir) with
                | Result.Ok() ->
                    fallbackManager <- Some manager
                    isActive <- true
                    isUsingFallback <- true

                    // フォールバック表示ループの開始
                    let! _ = Task.Run<Task<unit>>(fun () -> this.StartFallbackDisplay(manager))

                    logInfo "SessionBridge" (sprintf "フォールバックセッション開始成功: sessionId=%s" sessionId)
                    return Result.Ok()

                | Result.Error errorMsg ->
                    (manager :> IDisposable).Dispose()
                    return Result.Error(sprintf "フォールバック起動失敗: %s" errorMsg)

            with ex ->
                return Result.Error(sprintf "フォールバックセッション例外: %s" ex.Message)
        }

    /// フォールバック表示ループ
    member private this.StartFallbackDisplay(manager: FallbackProcessManager) : Task<unit> =
        task {
            try
                logInfo "SessionBridge" "フォールバック表示ループ開始"
                let mutable lastOutput = ""

                while isActive && manager.IsProcessAlive do
                    // フォールバック出力バッファから新しいデータを取得
                    let currentOutput = manager.GetOutput()

                    // 新しい出力がある場合のみUI更新
                    if currentOutput <> lastOutput then
                        Application.MainLoop.Invoke(fun () ->
                            // TextView更新（メインスレッドで実行）
                            pane.Text <- NStack.ustring.Make(currentOutput)
                            pane.SetNeedsDisplay())

                        lastOutput <- currentOutput
                        logDebug "SessionBridge" (sprintf "フォールバック表示更新: %d文字" currentOutput.Length)

                    // CPU使用率制御（100ms間隔）
                    do! Task.Delay(100)

                if not manager.IsProcessAlive then
                    logWarning "SessionBridge" "フォールバックプロセスが予期せず終了しました"
                    isActive <- false

            with ex ->
                logError "SessionBridge" (sprintf "フォールバック表示ループエラー: %s" ex.Message)
                isActive <- false
        }

    /// フォールバックモード通知をUI表示
    member private this.ShowFallbackNotification() : unit =
        Application.MainLoop.Invoke(fun () ->
            let notification = "[フォールバックモード] PTYが利用できないため、標準プロセスを使用しています\n\n"
            pane.Text <- NStack.ustring.Make(notification)
            pane.SetNeedsDisplay())

        logInfo "SessionBridge" "フォールバックモード通知を表示"

    /// 入力送信（PTYまたはフォールバック）
    member this.SendInput(input: string) : bool =
        if isActive then
            if isUsingFallback then
                // フォールバックモード
                match fallbackManager with
                | Some manager ->
                    try
                        let result = manager.SendInput(input)

                        if result then
                            let cleanInput = input.Replace("\n", "\\n")
                            logDebug "SessionBridge" (sprintf "フォールバック入力送信成功: %s" cleanInput)
                        else
                            logWarning "SessionBridge" "フォールバック入力送信失敗"

                        result
                    with ex ->
                        logError "SessionBridge" (sprintf "フォールバック入力送信例外: %s" ex.Message)
                        false
                | None ->
                    logWarning "SessionBridge" "アクティブなフォールバックマネージャーが存在しません"
                    false
            else
                // PTYモード
                match ptyManager with
                | Some manager ->
                    try
                        let result = manager.SendInput(input)

                        if result then
                            let cleanInput = input.Replace("\n", "\\n")
                            logDebug "SessionBridge" (sprintf "PTY入力送信成功: %s" cleanInput)
                        else
                            logWarning "SessionBridge" "PTY入力送信失敗"

                        result
                    with ex ->
                        logError "SessionBridge" (sprintf "PTY入力送信例外: %s" ex.Message)
                        false
                | None ->
                    logWarning "SessionBridge" "アクティブなPTYマネージャーが存在しません"
                    false
        else
            logWarning "SessionBridge" "アクティブなセッションが存在しません"
            false

    /// セッション終了（PTYまたはフォールバック）
    member this.StopSession() : unit =
        try
            logInfo "SessionBridge" "セッション終了開始"
            isActive <- false

            // PTYマネージャーの終了
            match ptyManager with
            | Some manager ->
                manager.CloseSession()
                (manager :> IDisposable).Dispose()
                ptyManager <- None
                logInfo "SessionBridge" "PTYセッション終了完了"
            | None -> logDebug "SessionBridge" "終了するPTYセッションがありません"

            // フォールバックマネージャーの終了
            match fallbackManager with
            | Some manager ->
                (manager :> IDisposable).Dispose()
                fallbackManager <- None
                logInfo "SessionBridge" "フォールバックセッション終了完了"
            | None -> logDebug "SessionBridge" "終了するフォールバックセッションがありません"

            isUsingFallback <- false

        with ex ->
            logError "SessionBridge" (sprintf "セッション終了エラー: %s" ex.Message)

    /// セッション状態確認
    member this.IsActive: bool = isActive

    /// 現在の出力を取得（デバッグ用）
    member this.GetCurrentOutput() : string =
        if isUsingFallback then
            match fallbackManager with
            | Some manager -> manager.GetOutput()
            | None -> ""
        else
            match ptyManager with
            | Some manager -> manager.GetOutput()
            | None -> ""

    /// 現在のモード確認
    member this.IsUsingFallback: bool = isUsingFallback

    /// セッション状態の詳細情報
    member this.GetSessionStatus() : string =
        if not isActive then
            "Inactive"
        elif isUsingFallback then
            match fallbackManager with
            | Some manager when manager.IsProcessAlive -> "Fallback Active"
            | Some _ -> "Fallback Stopped"
            | None -> "Fallback Error"
        else
            match ptyManager with
            | Some _ -> "PTY Active"
            | None -> "PTY Error"

    /// リソース解放
    interface IDisposable with
        member this.Dispose() = this.StopSession()
