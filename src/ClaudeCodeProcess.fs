module FCode.ClaudeCodeProcess

open System
open System.Diagnostics
open System.IO
open System.Threading.Tasks
open System.Text
open Terminal.Gui
open FCode.Logger

type ClaudeSession =
    { Process: Process option
      PaneId: string
      WorkingDirectory: string
      IsActive: bool
      OutputView: TextView option
      OutputBuffer: StringBuilder }

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
                // Claude CLI実行可能性確認
                logDebug "SessionManager" "Checking Claude CLI availability"
                let checkClaudeCmd = ProcessStartInfo("which", "claude")
                checkClaudeCmd.UseShellExecute <- false
                checkClaudeCmd.RedirectStandardOutput <- true
                checkClaudeCmd.CreateNoWindow <- true

                use checkProcess = Process.Start(checkClaudeCmd)
                checkProcess.WaitForExit()

                if checkProcess.ExitCode <> 0 then
                    let errorMsg =
                        "[ERROR] Claude CLI が見つかりません。インストールしてください: curl -fsSL https://claude.ai/cli.sh | sh"

                    logError "SessionManager" errorMsg
                    outputView.Text <- errorMsg
                    outputView.SetNeedsDisplay()
                    Application.Refresh()
                    false
                else
                    logDebug "SessionManager" $"Creating ProcessStartInfo for pane: {paneId}"
                    let startInfo = ProcessStartInfo()
                    startInfo.FileName <- "claude"
                    startInfo.WorkingDirectory <- workingDir
                    startInfo.UseShellExecute <- false
                    startInfo.RedirectStandardInput <- true
                    startInfo.RedirectStandardOutput <- true
                    startInfo.RedirectStandardError <- true
                    startInfo.CreateNoWindow <- true

                    // Claude Codeが対話式で動作するよう環境変数を設定
                    startInfo.Environment.["TERM"] <- "xterm-256color"
                    startInfo.Environment.["FORCE_COLOR"] <- "1"
                    startInfo.Environment.["NO_COLOR"] <- "0"

                    // FC-005: ペインロール情報を環境変数で設定
                    let role =
                        match paneId with
                        | id when id.StartsWith("dev") -> "dev"
                        | id when id.StartsWith("qa") -> "qa"
                        | "ux" -> "ux"
                        | "pm" -> "pm"
                        | _ -> "unknown"

                    startInfo.Environment.["CLAUDE_ROLE"] <- role
                    logDebug "SessionManager" $"Setting CLAUDE_ROLE={role} for pane: {paneId}"

                    logDebug "SessionManager" $"Starting Claude process for pane: {paneId}"
                    let proc = Process.Start(startInfo)
                    logInfo "SessionManager" $"Claude process started - PaneId: {paneId}, ProcessId: {proc.Id}"

                    let buffer = StringBuilder()

                    // UI更新頻度制限のためのタイマー
                    let mutable lastUiUpdate = DateTime.Now
                    let uiUpdateThresholdMs = 100 // 100ms間隔制限

                    // 標準出力の非同期読み取り設定
                    proc.OutputDataReceived.Add(fun args ->
                        if not (isNull args.Data) then
                            logDebug $"Claude-{paneId}" $"STDOUT: {args.Data}"
                            buffer.AppendLine($"[OUT] {args.Data}") |> ignore

                            // UI更新頻度制限
                            let now = DateTime.Now

                            if (now - lastUiUpdate).TotalMilliseconds > float uiUpdateThresholdMs then
                                outputView.Text <- buffer.ToString()
                                outputView.SetNeedsDisplay()
                                Application.Refresh()
                                lastUiUpdate <- now)

                    // 標準エラーの非同期読み取り設定（UI更新頻度制限共有）
                    proc.ErrorDataReceived.Add(fun args ->
                        if not (isNull args.Data) then
                            logError $"Claude-{paneId}" $"STDERR: {args.Data}"
                            buffer.AppendLine($"[ERR] {args.Data}") |> ignore

                            // UI更新頻度制限
                            let now = DateTime.Now

                            if (now - lastUiUpdate).TotalMilliseconds > float uiUpdateThresholdMs then
                                outputView.Text <- buffer.ToString()
                                outputView.SetNeedsDisplay()
                                Application.Refresh()
                                lastUiUpdate <- now)

                    logDebug "SessionManager" $"Starting async read for pane: {paneId}"
                    proc.BeginOutputReadLine()
                    proc.BeginErrorReadLine()

                    let session =
                        { Process = Some proc
                          PaneId = paneId
                          WorkingDirectory = workingDir
                          IsActive = true
                          OutputView = Some outputView
                          OutputBuffer = buffer }

                    sessions <- sessions.Add(paneId, session)
                    logInfo "SessionManager" $"Session created and stored for pane: {paneId}"

                    // 初期メッセージを表示
                    buffer.AppendLine($"[DEBUG] Claude Code セッション開始完了 - ペイン: {paneId}") |> ignore
                    buffer.AppendLine($"[DEBUG] 作業ディレクトリ: {workingDir}") |> ignore
                    buffer.AppendLine($"[DEBUG] プロセスID: {proc.Id}") |> ignore
                    buffer.AppendLine($"[DEBUG] ログファイル: {logger.LogPath}") |> ignore
                    buffer.AppendLine("=" + String.replicate 50 "=") |> ignore
                    buffer.AppendLine($"[INFO] Claude対話セッション初期化中...") |> ignore
                    outputView.Text <- buffer.ToString()
                    outputView.SetNeedsDisplay()

                    // 画面更新を強制
                    Application.Refresh()
                    logInfo "SessionManager" $"UI updated for pane: {paneId}"

                    // Claude Codeの対話モードを開始するため役割別初期プロンプトを送信
                    try
                        let rolePrompt =
                            match paneId with
                            | id when id.StartsWith("qa") ->
                                "こんにちは。私は品質保証の専門家として対話を開始します。"
                                + "テスト戦略、バグ検出、品質向上の観点から支援します。"
                                + "現在のプロジェクトのテスト状況と品質課題について教えてください。"
                            | id when id.StartsWith("dev") ->
                                "こんにちは。熟練のソフトウェアエンジニアとして対話を開始します。"
                                + "コード品質、パフォーマンス、保守性を重視して支援します。"
                                + "現在の開発状況と技術的課題について教えてください。"
                            | "ux" ->
                                "こんにちは。UX/UIデザインの専門家として対話を開始します。"
                                + "ユーザビリティ、アクセシビリティ、使いやすさの観点から支援します。"
                                + "現在のプロダクトのUX課題について教えてください。"
                            | "pm" ->
                                "こんにちは。プロジェクトマネージャーとして対話を開始します。"
                                + "進捗管理、リスク管理、品質管理の観点から支援します。"
                                + "現在のプロジェクト状況と課題について教えてください。"
                            | _ -> "こんにちは。対話を開始します。現在の作業ディレクトリとプロジェクト状況を教えてください。"

                        let initPrompt = rolePrompt
                        proc.StandardInput.WriteLine(initPrompt)
                        proc.StandardInput.Flush()
                        buffer.AppendLine($"> {initPrompt}") |> ignore
                        outputView.Text <- buffer.ToString()
                        outputView.SetNeedsDisplay()
                        Application.Refresh()
                        logInfo "SessionManager" $"Initial prompt sent to Claude Code for pane: {paneId}"
                    with ex ->
                        logError "SessionManager" $"Failed to send initial prompt to pane {paneId}: {ex.Message}"
                        buffer.AppendLine($"[ERROR] 初期プロンプト送信失敗: {ex.Message}") |> ignore
                        outputView.Text <- buffer.ToString()
                        outputView.SetNeedsDisplay()

                    true
            with ex ->
                logException "SessionManager" $"Failed to start session for pane: {paneId}" ex

                let errorMsg =
                    $"[ERROR] Claude Code起動エラー: {ex.Message}\n[DEBUG] StackTrace: {ex.StackTrace}\n[DEBUG] ログファイル: {logger.LogPath}"

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

                    let updatedSession =
                        { session with
                            Process = None
                            IsActive = false }

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
                with ex ->
                    logException "SessionManager" $"Failed to stop session for pane: {paneId}" ex

                    MessageBox.ErrorQuery("Error", $"Claude Code終了エラー: {ex.Message}", "OK")
                    |> ignore

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
                with ex ->
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
        sessions |> Map.filter (fun _ session -> session.IsActive) |> Map.count

    member this.CleanupAllSessions() =
        sessions |> Map.iter (fun paneId _ -> this.StopSession(paneId) |> ignore)
        sessions <- Map.empty

// Global session manager instance
let sessionManager = SessionManager()
