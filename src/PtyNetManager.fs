namespace FCode

open System
open System.Text
open System.IO
open System.Threading
open System.Threading.Tasks
open System.Diagnostics
open FCode.Logger
open Pty.Net

/// PTYセッション情報（Pty.Net使用版→フォールバック対応）
type PtySession =
    { PtyConnection: IPtyConnection option
      Process: Process option
      ProcessId: int
      IsRunning: bool
      mutable OutputBuffer: StringBuilder
      CancellationTokenSource: CancellationTokenSource }

/// PTY管理とパフォーマンス計測のためのマネージャー（.NET Process使用）
type PtyNetManager() =
    let mutable currentSession: PtySession option = None
    let outputBufferLock = obj ()

    /// PTYセッションを新規作成（Pty.Net使用版→フォールバック統合）
    member this.CreateSession(command: string, args: string[]) : Task<Result<PtySession, string>> =
        task {
            try
                // Pty.Net試行（現在のバージョンでは失敗予定）
                logInfo "PTYセッション作成試行" $"command={command}, trying Pty.Net first"

                // 現在のPty.Net 0.1.16-preは不安定なため、
                // 基盤実装としてフォールバック処理を使用
                return! this.CreateFallbackSession(command, args)

            with ex ->
                logError "PTYセッション作成エラー" ex.Message
                // フォールバック処理
                return! this.CreateFallbackSession(command, args)
        }

    /// フォールバック: 通常のProcessを使用
    member private this.CreateFallbackSession(command: string, args: string[]) : Task<Result<PtySession, string>> =
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
                processInfo.Environment.Add("TERM", "xterm-256color")

                let proc = new Process()
                proc.StartInfo <- processInfo

                let session =
                    { PtyConnection = None
                      Process = Some proc
                      ProcessId = 0
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

                let started = proc.Start()

                if started then
                    proc.BeginOutputReadLine()
                    proc.BeginErrorReadLine()

                    let updatedSession =
                        { session with
                            ProcessId = proc.Id
                            IsRunning = true }

                    currentSession <- Some updatedSession
                    logInfo "Fallback PTYセッション作成成功" $"command={command}, pid={proc.Id}"
                    return Result.Ok updatedSession
                else
                    return Result.Error "プロセス開始に失敗しました"

            with ex ->
                logError "Fallback PTYセッション作成エラー" ex.Message
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

    /// 非同期出力読み取り（Process対応）
    member this.StartOutputReading() : Task<unit> =
        task {
            match currentSession with
            | Some session when session.IsRunning ->
                match session.Process with
                | Some _ ->
                    // Process使用時は既にイベントで処理済み
                    logInfo "出力読み取り開始" "Process.OutputDataReceivedで自動処理中"
                | None ->
                    // PTY使用時（現在は未実装）
                    logInfo "出力読み取り開始" "PTY出力読み取りは将来実装予定"
            | _ -> logWarning "出力読み取り開始" "アクティブなセッションが存在しません"
        }

    /// 出力読み取り（セッション付き）
    member private this.StartOutputReading(session: PtySession) : Task<unit> =
        task {
            match session.Process with
            | Some _ -> logInfo "出力読み取り開始" "Process.OutputDataReceivedで自動処理中"
            | None -> logInfo "出力読み取り開始" "PTY出力読み取りは将来実装予定"
        }

    /// 入力データ送信（Process対応）
    member this.SendInput(input: string) : bool =
        match currentSession with
        | Some session when session.IsRunning ->
            try
                match session.Process with
                | Some proc ->
                    // Process使用時の入力送信
                    proc.StandardInput.Write(input)
                    proc.StandardInput.Flush()
                    true
                | None ->
                    // PTY使用時（現在は未実装）
                    logWarning "PTY入力送信" "PTY入力送信は将来実装予定"
                    false
            with ex ->
                logError "PTY入力送信エラー" ex.Message
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

    /// セッション終了（Process対応）
    member this.CloseSession() : unit =
        match currentSession with
        | Some session ->
            session.CancellationTokenSource.Cancel()

            try
                match session.Process with
                | Some proc ->
                    // Process終了
                    if not proc.HasExited then
                        proc.Kill()

                    proc.Dispose()
                | None ->
                    // PTY接続終了（将来実装）
                    logInfo "PTY接続終了" "PTY接続終了は将来実装予定"
            with ex ->
                logError "セッション終了エラー" ex.Message

            logInfo "PTYセッション終了" $"ProcessId={session.ProcessId}"
            currentSession <- None
        | None -> ()

    // リソース解放
    interface IDisposable with
        member this.Dispose() = this.CloseSession()
