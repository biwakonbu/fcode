namespace FCode

open System
open System.IO
open System.Threading.Tasks
open Terminal.Gui
open FCode
open FCode.Logger

/// PTY出力ストリームをTerminal.Gui TextViewにリアルタイム表示する統合ブリッジ
type SessionBridge(pane: TextView) =
    let mutable ptyManager: PtyNetManager option = None
    let mutable isActive = false

    /// セッション開始: PTY作成とリアルタイム表示開始
    member this.StartSession
        (sessionId: string, command: string, args: string[], workingDir: string)
        : Task<Result<unit, string>> =
        task {
            try
                if isActive then
                    return Result.Error "既にアクティブなセッションが存在します"
                else
                    logInfo "SessionBridge" $"セッション開始: sessionId={sessionId}, command={command}"

                    // PTYマネージャーの作成
                    let manager = new PtyNetManager()

                    // PTYセッションの作成
                    let! sessionResult = manager.CreateSession(command, args)

                    match sessionResult with
                    | Result.Ok session ->
                        ptyManager <- Some manager
                        isActive <- true

                        // 出力読み取り開始
                        do! manager.StartOutputReading()

                        // リアルタイム表示ループの開始
                        let! _ = Task.Run<Task<unit>>(fun () -> this.StartRealtimeDisplay(manager))

                        logInfo "SessionBridge" $"セッション開始成功: sessionId={sessionId}"
                        return Result.Ok()

                    | Result.Error errorMsg ->
                        logError "SessionBridge" $"PTYセッション作成エラー: {errorMsg}"
                        return Result.Error errorMsg

            with ex ->
                logError "SessionBridge" $"セッション開始例外: {ex.Message}"
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
                        logDebug "SessionBridge" $"TextView更新: {currentOutput.Length}文字"

                    // CPU使用率制御（100ms間隔）
                    do! Task.Delay(100)

            with ex ->
                logError "SessionBridge" $"リアルタイム表示ループエラー: {ex.Message}"
                isActive <- false
        }

    /// PTYへの入力送信
    member this.SendInput(input: string) : bool =
        match ptyManager with
        | Some manager when isActive ->
            try
                let result = manager.SendInput(input)

                if result then
                    let cleanInput = input.Replace("\n", "\\n")
                    logDebug "SessionBridge" $"入力送信成功: {cleanInput}"
                else
                    logWarning "SessionBridge" "入力送信失敗"

                result
            with ex ->
                logError "SessionBridge" $"入力送信例外: {ex.Message}"
                false
        | _ ->
            logWarning "SessionBridge" "アクティブなPTYセッションが存在しません"
            false

    /// セッション終了
    member this.StopSession() : unit =
        try
            logInfo "SessionBridge" "セッション終了開始"
            isActive <- false

            match ptyManager with
            | Some manager ->
                manager.CloseSession()
                (manager :> IDisposable).Dispose()
                ptyManager <- None
                logInfo "SessionBridge" "PTYセッション終了完了"
            | None -> logInfo "SessionBridge" "終了するPTYセッションがありません"

        with ex ->
            logError "SessionBridge" $"セッション終了エラー: {ex.Message}"

    /// セッション状態確認
    member this.IsActive: bool = isActive

    /// 現在の出力を取得（デバッグ用）
    member this.GetCurrentOutput() : string =
        match ptyManager with
        | Some manager -> manager.GetOutput()
        | None -> ""

    /// リソース解放
    interface IDisposable with
        member this.Dispose() = this.StopSession()
