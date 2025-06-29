namespace FCode

open System
open System.Text
open System.IO
open System.Threading
open System.Threading.Tasks
open System.Diagnostics
open FCode.Logger

/// PTYセッション情報（.NET Process使用の簡易版）
type PtySession =
    { Process: Process
      ProcessId: int
      IsRunning: bool
      mutable OutputBuffer: StringBuilder
      CancellationTokenSource: CancellationTokenSource }

/// PTY管理とパフォーマンス計測のためのマネージャー（.NET Process使用）
type PtyNetManager() =
    let mutable currentSession: PtySession option = None
    let outputBufferLock = obj ()

    /// PTYセッションを新規作成（.NET Process使用）
    member this.CreateSession(command: string, args: string[]) : Task<Result<PtySession, string>> =
        task {
            try
                let processInfo = ProcessStartInfo()
                processInfo.FileName <- command
                processInfo.Arguments <- String.Join(" ", args)
                processInfo.UseShellExecute <- false
                processInfo.RedirectStandardInput <- true
                processInfo.RedirectStandardOutput <- true
                processInfo.RedirectStandardError <- true
                processInfo.CreateNoWindow <- true

                // 疑似ターミナル環境の設定
                processInfo.Environment.Add("TERM", "xterm-256color")
                processInfo.Environment.Add("COLUMNS", "80")
                processInfo.Environment.Add("LINES", "24")

                let proc = new Process()
                proc.StartInfo <- processInfo

                let session =
                    { Process = proc
                      ProcessId = 0 // 起動後に設定
                      IsRunning = false
                      OutputBuffer = StringBuilder()
                      CancellationTokenSource = new CancellationTokenSource() }

                // 出力イベントハンドラー設定
                proc.OutputDataReceived.Add(fun e ->
                    if not (String.IsNullOrEmpty(e.Data)) then
                        lock outputBufferLock (fun () -> session.OutputBuffer.AppendLine(e.Data) |> ignore))

                proc.ErrorDataReceived.Add(fun e ->
                    if not (String.IsNullOrEmpty(e.Data)) then
                        lock outputBufferLock (fun () -> session.OutputBuffer.AppendLine("[ERR] " + e.Data) |> ignore))

                // プロセス開始
                let started = proc.Start()

                if started then
                    proc.BeginOutputReadLine()
                    proc.BeginErrorReadLine()

                    let updatedSession =
                        { session with
                            ProcessId = proc.Id
                            IsRunning = true }

                    currentSession <- Some updatedSession
                    logInfo "PTYセッション作成成功" ("command=" + command + ", pid=" + proc.Id.ToString())
                    return Result.Ok updatedSession
                else
                    return Result.Error "プロセス開始に失敗しました"

            with ex ->
                logError "PTYセッション作成エラー" (ex.Message)
                return Result.Error(ex.Message)
        }

    /// ウィンドウサイズ変更（SIGWINCH検証用）
    member this.ResizeWindow(rows: int, cols: int) : bool =
        match currentSession with
        | Some session when session.IsRunning ->
            try
                // 環境変数でウィンドウサイズを設定（簡易版）
                Environment.SetEnvironmentVariable("COLUMNS", cols.ToString())
                Environment.SetEnvironmentVariable("LINES", rows.ToString())

                // SIGWINCHシミュレーション：Ctrl+Lを送信して画面再描画を促す
                this.SendInput("\u000c") |> ignore // Ctrl+L (Form Feed)

                logInfo "ウィンドウリサイズ" ("rows=" + rows.ToString() + ", cols=" + cols.ToString())
                true
            with ex ->
                logError "ウィンドウリサイズエラー" (ex.Message)
                false
        | _ -> false

    /// 非同期出力読み取り（スループット計測用）
    member this.StartOutputReading() : Task<unit> =
        task {
            match currentSession with
            | Some session when session.IsRunning ->
                // Process.OutputDataReceivedイベントで既に非同期読み取り開始済み
                // このメソッドは互換性のために存在
                logInfo "出力読み取り開始" "Process.OutputDataReceivedで自動処理中"
            | _ -> logWarning "出力読み取り開始" "アクティブなセッションが存在しません"
        }

    /// 入力データ送信
    member this.SendInput(input: string) : bool =
        match currentSession with
        | Some session when session.IsRunning ->
            try
                session.Process.StandardInput.Write(input)
                session.Process.StandardInput.Flush()
                true
            with ex ->
                logError "PTY入力送信エラー" (ex.Message)
                false
        | _ -> false

    /// 現在の出力バッファを取得
    member this.GetOutput() : string =
        match currentSession with
        | Some session -> lock outputBufferLock (fun () -> session.OutputBuffer.ToString())
        | None -> ""

    /// 出力バッファをクリア
    member this.ClearOutput() : unit =
        match currentSession with
        | Some session -> lock outputBufferLock (fun () -> session.OutputBuffer.Clear() |> ignore)
        | None -> ()

    /// セッション終了
    member this.CloseSession() : unit =
        match currentSession with
        | Some session ->
            session.CancellationTokenSource.Cancel()

            try
                if not session.Process.HasExited then
                    session.Process.Kill()

                session.Process.Dispose()
            with ex ->
                logError "セッション終了エラー" (ex.Message)

            logInfo "PTYセッション終了" ("ProcessId=" + session.ProcessId.ToString())
            currentSession <- None
        | None -> ()

    // リソース解放
    interface IDisposable with
        member this.Dispose() = this.CloseSession()
