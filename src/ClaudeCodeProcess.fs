module TuiPoC.ClaudeCodeProcess

open System
open System.Diagnostics
open System.IO
open System.Threading.Tasks
open System.Text
open Terminal.Gui
open TuiPoC.Logger

type ClaudeSession = {
    Process: Process option
    PaneId: string
    WorkingDirectory: string
    IsActive: bool
    OutputView: TextView option
    OutputBuffer: StringBuilder
}

type SessionManager() =
    let mutable sessions = Map.empty<string, ClaudeSession>
    
    member _.StartSession(paneId: string, workingDir: string, outputView: TextView) =
        logInfo "SessionManager" $"StartSession called - PaneId: {paneId}, WorkingDir: {workingDir}"
        
        match sessions.TryFind(paneId) with
        | Some session when session.IsActive ->
            logWarning "SessionManager" $"Session already active for pane: {paneId}"
            false // Session already running
        | _ ->
            try
                logDebug "SessionManager" $"Creating ProcessStartInfo for pane: {paneId}"
                let startInfo = ProcessStartInfo()
                startInfo.FileName <- "claude"
                startInfo.WorkingDirectory <- workingDir
                startInfo.UseShellExecute <- false
                startInfo.RedirectStandardInput <- true
                startInfo.RedirectStandardOutput <- true
                startInfo.RedirectStandardError <- true
                startInfo.CreateNoWindow <- true
                
                logDebug "SessionManager" $"Starting Claude process for pane: {paneId}"
                let proc = Process.Start(startInfo)
                logInfo "SessionManager" $"Claude process started - PaneId: {paneId}, ProcessId: {proc.Id}"
                
                let buffer = StringBuilder()
                
                // 標準出力の非同期読み取り設定
                proc.OutputDataReceived.Add(fun args ->
                    if not (isNull args.Data) then
                        logDebug $"Claude-{paneId}" $"STDOUT: {args.Data}"
                        buffer.AppendLine($"[OUT] {args.Data}") |> ignore
                        outputView.Text <- buffer.ToString()
                        outputView.SetNeedsDisplay()
                        Application.Refresh())
                
                // 標準エラーの非同期読み取り設定
                proc.ErrorDataReceived.Add(fun args ->
                    if not (isNull args.Data) then
                        logError $"Claude-{paneId}" $"STDERR: {args.Data}"
                        buffer.AppendLine($"[ERR] {args.Data}") |> ignore
                        outputView.Text <- buffer.ToString()
                        outputView.SetNeedsDisplay()
                        Application.Refresh())
                
                logDebug "SessionManager" $"Starting async read for pane: {paneId}"
                proc.BeginOutputReadLine()
                proc.BeginErrorReadLine()
                
                let session = {
                    Process = Some proc
                    PaneId = paneId
                    WorkingDirectory = workingDir
                    IsActive = true
                    OutputView = Some outputView
                    OutputBuffer = buffer
                }
                
                sessions <- sessions.Add(paneId, session)
                logInfo "SessionManager" $"Session created and stored for pane: {paneId}"
                
                // 初期メッセージを表示
                buffer.AppendLine($"[DEBUG] Claude Code セッション開始完了 - ペイン: {paneId}") |> ignore
                buffer.AppendLine($"[DEBUG] 作業ディレクトリ: {workingDir}") |> ignore
                buffer.AppendLine($"[DEBUG] プロセスID: {proc.Id}") |> ignore
                buffer.AppendLine($"[DEBUG] ログファイル: {logger.LogPath}") |> ignore
                buffer.AppendLine("=" + String.replicate 50 "=") |> ignore
                outputView.Text <- buffer.ToString()
                outputView.SetNeedsDisplay()
                
                // 画面更新を強制
                Application.Refresh()
                logInfo "SessionManager" $"UI updated for pane: {paneId}"
                
                true
            with
            | ex ->
                logException "SessionManager" $"Failed to start session for pane: {paneId}" ex
                let errorMsg = $"[ERROR] Claude Code起動エラー: {ex.Message}\n[DEBUG] StackTrace: {ex.StackTrace}\n[DEBUG] ログファイル: {logger.LogPath}"
                outputView.Text <- errorMsg
                outputView.SetNeedsDisplay()
                Application.Refresh()
                false
    
    member _.StopSession(paneId: string) =
        logInfo "SessionManager" $"StopSession called for pane: {paneId}"
        match sessions.TryFind(paneId) with
        | Some session when session.IsActive ->
            match session.Process with
            | Some proc ->
                try
                    logDebug "SessionManager" $"Stopping process for pane: {paneId}, ProcessId: {proc.Id}"
                    if not proc.HasExited then
                        proc.CloseMainWindow() |> ignore
                        if not (proc.WaitForExit(3000)) then
                            logWarning "SessionManager" $"Force killing process for pane: {paneId}"
                            proc.Kill()
                    proc.Dispose()
                    
                    let updatedSession = { session with Process = None; IsActive = false }
                    sessions <- sessions.Add(paneId, updatedSession)
                    logInfo "SessionManager" $"Session stopped for pane: {paneId}"
                    
                    // 終了メッセージを表示
                    match session.OutputView with
                    | Some outputView ->
                        session.OutputBuffer.AppendLine("Claude Code セッション終了") |> ignore
                        outputView.Text <- session.OutputBuffer.ToString()
                        outputView.SetNeedsDisplay()
                    | None -> ()
                    true
                with
                | ex ->
                    logException "SessionManager" $"Failed to stop session for pane: {paneId}" ex
                    MessageBox.ErrorQuery("Error", $"Claude Code終了エラー: {ex.Message}", "OK") |> ignore
                    false
            | None -> 
                logWarning "SessionManager" $"No process found for pane: {paneId}"
                false
        | _ -> 
            logWarning "SessionManager" $"No active session found for pane: {paneId}"
            false
    
    member _.SendInput(paneId: string, input: string) =
        logDebug "SessionManager" $"SendInput called for pane: {paneId}, input: {input}"
        match sessions.TryFind(paneId) with
        | Some session when session.IsActive ->
            match session.Process with
            | Some proc when not proc.HasExited ->
                try
                    // 入力内容をペインに表示
                    session.OutputBuffer.AppendLine($"> {input}") |> ignore
                    match session.OutputView with
                    | Some outputView ->
                        outputView.Text <- session.OutputBuffer.ToString()
                        outputView.SetNeedsDisplay()
                    | None -> ()
                    
                    proc.StandardInput.WriteLine(input)
                    proc.StandardInput.Flush()
                    logDebug "SessionManager" $"Input sent to pane: {paneId}"
                    true
                with
                | ex ->
                    logException "SessionManager" $"Failed to send input to pane: {paneId}" ex
                    false
            | _ -> 
                logWarning "SessionManager" $"Process not available for input to pane: {paneId}"
                false
        | _ -> 
            logWarning "SessionManager" $"No active session for input to pane: {paneId}"
            false
    
    member _.IsSessionActive(paneId: string) =
        match sessions.TryFind(paneId) with
        | Some session -> session.IsActive
        | None -> false
    
    member _.GetActiveSessionCount() =
        sessions 
        |> Map.filter (fun _ session -> session.IsActive)
        |> Map.count
    
    member this.CleanupAllSessions() =
        sessions
        |> Map.iter (fun paneId _ -> 
            this.StopSession(paneId) |> ignore)
        sessions <- Map.empty

// Global session manager instance
let sessionManager = SessionManager()