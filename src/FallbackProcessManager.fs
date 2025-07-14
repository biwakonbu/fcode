namespace FCode

open System
open System.Diagnostics
open System.IO
open System.Threading.Tasks
open FCode.Logger

/// PTY失敗時の標準Process実行によるフォールバック機能
type FallbackProcessManager() =
    let mutable currentProcess: Process option = None
    let mutable isActive = false
    let mutable outputBuffer = System.Text.StringBuilder()

    /// フォールバックプロセスの開始
    member this.StartProcess
        (sessionId: string, command: string, args: string[], workingDir: string)
        : Result<unit, string> =
        try
            if isActive then
                Result.Error "既にアクティブなフォールバックプロセスが存在します"
            else
                logInfo "FallbackProcessManager" (sprintf "フォールバックプロセス開始: sessionId=%s, command=%s" sessionId command)

                let psi =
                    ProcessStartInfo(
                        FileName = command,
                        Arguments = String.Join(" ", args),
                        WorkingDirectory = workingDir,
                        UseShellExecute = false,
                        RedirectStandardInput = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    )

                let newProcess = Process.Start(psi)
                currentProcess <- Some newProcess
                isActive <- true

                // 標準出力・エラー出力の非同期読み取り開始
                newProcess.OutputDataReceived.Add(this.OnOutputReceived)
                newProcess.ErrorDataReceived.Add(this.OnErrorReceived)
                newProcess.BeginOutputReadLine()
                newProcess.BeginErrorReadLine()

                logInfo
                    "FallbackProcessManager"
                    (sprintf "フォールバックプロセス開始成功: sessionId=%s, PID=%d" sessionId newProcess.Id)

                Result.Ok()

        with ex ->
            logError "FallbackProcessManager" (sprintf "フォールバックプロセス開始失敗: %s" ex.Message)
            Result.Error ex.Message

    /// 標準出力受信イベントハンドラ
    member private this.OnOutputReceived(e: DataReceivedEventArgs) : unit =
        if not (isNull e.Data) then
            outputBuffer.AppendLine(e.Data) |> ignore
            logDebug "FallbackProcessManager" (sprintf "標準出力受信: %s" e.Data)

    /// 標準エラー受信イベントハンドラ
    member private this.OnErrorReceived(e: DataReceivedEventArgs) : unit =
        if not (isNull e.Data) then
            outputBuffer.AppendLine(sprintf "[STDERR] %s" e.Data) |> ignore
            logWarning "FallbackProcessManager" (sprintf "標準エラー受信: %s" e.Data)

    /// プロセスへの入力送信
    member this.SendInput(input: string) : bool =
        match currentProcess with
        | Some proc when isActive && not proc.HasExited ->
            try
                proc.StandardInput.Write(input)
                proc.StandardInput.Flush()
                logDebug "FallbackProcessManager" (sprintf "入力送信成功: %s" (input.Replace("\n", "\\n")))
                true
            with ex ->
                logError "FallbackProcessManager" (sprintf "入力送信失敗: %s" ex.Message)
                false
        | _ ->
            logWarning "FallbackProcessManager" "アクティブなフォールバックプロセスが存在しません"
            false

    /// 現在の出力バッファを取得
    member this.GetOutput() : string = outputBuffer.ToString()

    /// 出力バッファをクリア
    member this.ClearOutput() : unit = outputBuffer.Clear() |> ignore

    /// プロセスの停止
    member this.StopProcess() : unit =
        try
            logInfo "FallbackProcessManager" "フォールバックプロセス停止開始"
            isActive <- false

            match currentProcess with
            | Some proc ->
                if not proc.HasExited then
                    proc.Kill()

                    if not (proc.WaitForExit(5000)) then
                        logWarning "FallbackProcessManager" "プロセス強制終了タイムアウト"

                proc.Dispose()
                currentProcess <- None
                logInfo "FallbackProcessManager" "フォールバックプロセス停止完了"
            | None -> logInfo "FallbackProcessManager" "停止するフォールバックプロセスがありません"

        with ex ->
            logError "FallbackProcessManager" (sprintf "フォールバックプロセス停止エラー: %s" ex.Message)

    /// セッション状態確認
    member this.IsActive: bool = isActive

    /// プロセス存在確認
    member this.IsProcessAlive: bool =
        match currentProcess with
        | Some proc -> not proc.HasExited
        | None -> false

    /// リソース解放
    interface IDisposable with
        member this.Dispose() = this.StopProcess()
