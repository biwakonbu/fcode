module FCode.Program

open System.Threading.Tasks
open Terminal.Gui
open FCode.Logger
open FCode.ColorSchemes
open FCode.KeyBindings
open FCode.ClaudeCodeProcess
open FCode.WorkerProcessManager
open FCode.UIHelpers

[<EntryPoint>]
let main argv =
    try
        logInfo "Application" "=== fcode TUI Application Starting ==="
        let argsString = System.String.Join(" ", argv)
        logInfo "Application" $"Command line args: {argsString}"

        // Check if running in CI environment
        let isCI = not (isNull (System.Environment.GetEnvironmentVariable("CI")))

        if isCI then
            logInfo "Application" "Running in CI environment - skipping Terminal.Gui initialization"
            0 // Exit successfully in CI
        else
            // Initialize application
            logDebug "Application" "Initializing Terminal.Gui"
            Application.Init()
            logInfo "Application" "Terminal.Gui initialized successfully"

            // Start ProcessSupervisor for worker process management
            logInfo "Application" "Starting ProcessSupervisor for worker process management"
            workerManager.StartSupervisor()
            logInfo "Application" "ProcessSupervisor started successfully"

            let top = Application.Top
            logDebug "Application" "Got Application.Top"

            // --- Conversation Pane -------------------------------------------------
            logDebug "UI" "Creating conversation pane"
            let conversationWidth = 60 // columns
            let convo = new FrameView("会話")
            convo.X <- 0
            convo.Y <- 0
            convo.Width <- conversationWidth
            convo.Height <- Dim.Fill()

            // Border-less style for the conversation pane (フラット表示)
            convo.Border.Effect3D <- false
            // Remove title bar completely for flat display
            convo.Title <- ""
            // Apply chat color scheme
            applySchemeByRole convo "chat"
            logInfo "UI" "Conversation pane created successfully"

            // ----------------------------------------------------------------------
            // Right-hand container – holds all other panes
            let right = new View()
            right.X <- Pos.Right convo
            right.Y <- 0
            right.Width <- Dim.Fill()
            right.Height <- Dim.Fill()

            // TextView直接参照用マップ
            let mutable paneTextViews = Map.empty<string, TextView>

            // Helper function to create a pane with a given title and TextView
            let makePane title =
                logDebug "UI" $"Creating pane: {title}"
                let fv = new FrameView(title: string)
                fv.Border.Effect3D <- false
                // Apply color scheme based on title
                applySchemeByRole fv title

                // エージェントペインの場合はTextViewを追加
                if title <> "会話" then
                    logDebug "UI" $"Adding TextView to pane: {title}"
                    let textView = new TextView()
                    textView.X <- 0
                    textView.Y <- 0
                    textView.Width <- Dim.Fill()
                    textView.Height <- Dim.Fill()
                    textView.ReadOnly <- true
                    textView.Text <- $"[DEBUG] {title}ペイン - TextView初期化完了\n[DEBUG] Claude Code初期化準備中..."

                    // Terminal.Gui 1.15.0の推奨方法: Add()メソッド使用
                    fv.Add(textView)

                    // 追加後に適切にレイアウト
                    textView.SetNeedsDisplay()
                    fv.SetNeedsDisplay()

                    // TextView直接参照用マップに追加
                    paneTextViews <- paneTextViews.Add(title, textView)

                    logInfo "UI" $"TextView added to pane: {title} - Subviews count: {fv.Subviews.Count}"
                    logDebug "UI" $"TextView type: {textView.GetType().Name}"
                    logInfo "UI" $"TextView stored in direct reference map for pane: {title}"

                    // 追加の検証: 追加されたTextViewが実際に見つかるかテスト
                    let verifyTextViews = getTextViewsFromPane fv
                    logInfo "UI" $"Verification: Found {verifyTextViews.Length} TextViews in newly created pane {title}"

                logDebug "UI" $"Pane created: {title}"
                fv

            // Row heights (percentage of right-hand container)
            let devRowHeight = Dim.Percent 40.0f // 上段: dev1-3
            let qaRowHeight = Dim.Percent 40.0f // 中段: qa1-2, ux

            // ------------------------------------------------------------------
            // Top row – dev1 dev2 dev3
            let dev1 = makePane "dev1"
            dev1.X <- 0
            dev1.Y <- 0
            dev1.Width <- Dim.Percent 33.0f
            dev1.Height <- devRowHeight

            let dev2 = makePane "dev2"
            dev2.X <- Pos.Right dev1
            dev2.Y <- 0
            dev2.Width <- Dim.Percent 33.0f
            dev2.Height <- devRowHeight

            let dev3 = makePane "dev3"
            dev3.X <- Pos.Right dev2
            dev3.Y <- 0
            dev3.Width <- Dim.Fill() // remainder of width
            dev3.Height <- devRowHeight

            // ------------------------------------------------------------------
            // Middle row – qa1 qa2 ux
            let qa1 = makePane "qa1"
            qa1.X <- 0
            qa1.Y <- Pos.Bottom dev1
            qa1.Width <- Dim.Percent 33.0f
            qa1.Height <- qaRowHeight

            let qa2 = makePane "qa2"
            qa2.X <- Pos.Right qa1
            qa2.Y <- qa1.Y
            qa2.Width <- Dim.Percent 33.0f
            qa2.Height <- qaRowHeight

            let ux = makePane "ux"
            ux.X <- Pos.Right qa2
            ux.Y <- qa1.Y
            ux.Width <- Dim.Fill()
            ux.Height <- qaRowHeight

            // ------------------------------------------------------------------
            // Bottom row – PM / PdM timeline spanning full width
            let timeline = makePane "PM / PdM タイムライン"
            timeline.X <- 0
            timeline.Y <- Pos.Bottom qa1
            timeline.Width <- Dim.Fill()
            timeline.Height <- Dim.Fill()
            // Apply PM color scheme specifically
            applySchemeByRole timeline "pm"

            // Add panes to right container
            right.Add(dev1, dev2, dev3, qa1, qa2, ux, timeline)

            // Add top-level panes
            top.Add(convo, right)

            // エージェントペインでのClaude Code自動起動
            let agentPanes =
                [ ("dev1", dev1)
                  ("dev2", dev2)
                  ("dev3", dev3)
                  ("qa1", qa1)
                  ("qa2", qa2)
                  ("ux", ux)
                  ("pm", timeline) ]

            let startClaudeCodeForPane (paneId: string, pane: FrameView) =
                logInfo "AutoStart" $"Starting Claude Code for pane: {paneId}"

                // 直接参照マップからTextViewを取得
                match paneTextViews.TryFind(paneId) with
                | Some textView ->
                    logInfo "AutoStart" $"TextView found via direct reference for pane: {paneId}"

                    try
                        textView.Text <- $"[DEBUG] {paneId}ペイン - TextView発見、Claude Code起動開始..."
                        textView.SetNeedsDisplay()
                        Application.Refresh()

                        let workingDir = System.Environment.CurrentDirectory
                        let success = workerManager.StartWorker(paneId, workingDir, textView)

                        if not success then
                            logError "AutoStart" $"Failed to start Claude Code for pane: {paneId}"
                            textView.Text <- $"[ERROR] {paneId}ペイン - Claude Code起動失敗"
                            textView.SetNeedsDisplay()
                            Application.Refresh()
                        else
                            logInfo "AutoStart" $"Successfully started Claude Code for pane: {paneId}"
                    with ex ->
                        logError "AutoStart" $"Exception during Claude Code start for {paneId}: {ex.Message}"

                | None ->
                    // TextViewが見つからない場合（直接参照マップにない）
                    let debugMsg = $"[ERROR] {paneId}ペイン - TextView direct reference not found"
                    logError "AutoStart" debugMsg
                    System.Console.WriteLine(debugMsg)

                    // 根本調査: UI構造の詳細ダンプ
                    logInfo "AutoStart" $"=== ROOT CAUSE INVESTIGATION for {paneId} ==="
                    logInfo "AutoStart" $"Dumping complete UI structure for pane: {paneId}"
                    dumpViewHierarchy pane 0

                    // 改良されたfindTextViews関数でフォールバック検索
                    logInfo "AutoStart" $"Attempting improved TextView search for pane: {paneId}"
                    let textViews = getTextViewsFromPane pane

                    match textViews with
                    | textView :: _ ->
                        logInfo "AutoStart" $"TextView found via improved search for pane: {paneId}"

                        try
                            textView.Text <- $"[IMPROVED] {paneId}ペイン - TextView発見（改良検索）"
                            textView.SetNeedsDisplay()
                            Application.Refresh()
                        with ex ->
                            logError "AutoStart" $"Improved TextView access failed for {paneId}: {ex.Message}"
                    | [] ->
                        logError "AutoStart" $"No TextView found even with improved search for pane: {paneId}"
                        logError "AutoStart" $"=== ROOT CAUSE: UI structure investigation completed ==="

            // UI初期化完了後の遅延自動起動機能で実行するため、即座の自動起動は削除
            logInfo "AutoStart" "Immediate auto-start disabled - will use delayed auto-start after UI completion"

            // Create focus management for panes
            let focusablePanes = [| convo; dev1; dev2; dev3; qa1; qa2; ux; timeline |]

            // Create Emacs key handler
            let emacsKeyHandler = EmacsKeyHandler(focusablePanes, sessionManager)

            // Add Emacs-style key handling
            let keyHandler =
                System.Action<View.KeyEventEventArgs>(fun args ->
                    let handled = emacsKeyHandler.HandleKey(args.KeyEvent)
                    args.Handled <- handled)

            // Override key processing
            top.add_KeyDown keyHandler

            // Set initial focus
            logDebug "Application" "Setting initial focus to conversation pane"
            focusablePanes.[0].SetFocus()

            // Application.Run後の遅延起動を設定
            let setupDelayedAutoStart () =
                // Application.RunLoop開始後に安全にClaude Codeを起動
                Task.Run(fun () ->
                    logInfo "AutoStart" "Starting delayed auto-start after UI initialization"
                    System.Threading.Thread.Sleep(1000) // 1秒待機でUI完全初期化

                    // メインスレッドでUI操作を実行
                    Application.MainLoop.Invoke(fun () ->
                        logInfo "AutoStart" "Executing delayed Claude Code auto-start for dev and qa panes"
                        logInfo "AutoStart" "UI should be fully initialized at this point"

                        // dev1-3, qa1-2ペインを順次起動（500ms間隔で負荷分散）
                        let activeAgentPanes =
                            agentPanes
                            |> List.filter (fun (id, _) -> id.StartsWith("dev") || id.StartsWith("qa"))

                        logInfo
                            "AutoStart"
                            $"Found {activeAgentPanes.Length} active agent panes for delayed auto-start"

                        // 各ペインの状態を事前チェック
                        activeAgentPanes
                        |> List.iter (fun (paneId, pane) ->
                            logInfo "AutoStart" $"Pre-check pane {paneId}: Subviews={pane.Subviews.Count}")

                        activeAgentPanes
                        |> List.iteri (fun i (paneId, pane) ->
                            Task.Run(fun () ->
                                System.Threading.Thread.Sleep(i * 500) // 500ms間隔で起動

                                Application.MainLoop.Invoke(fun () ->
                                    logInfo
                                        "AutoStart"
                                        $"Starting delayed auto-start for {paneId} (step {i + 1}/{activeAgentPanes.Length})"

                                    startClaudeCodeForPane (paneId, pane)
                                    logInfo "AutoStart" $"Delayed auto-start completed for {paneId}"))
                            |> ignore)

                        logInfo "AutoStart" $"Delayed auto-start initiated for {activeAgentPanes.Length} active panes"))
                |> ignore

            // Run application
            logInfo "Application" "Starting TUI application loop"
            setupDelayedAutoStart ()
            Application.Run(top)
            logInfo "Application" "TUI application loop ended"

            // Cleanup
            logInfo "Application" "Cleaning up Worker Processes and sessions"
            workerManager.CleanupAllWorkers()
            workerManager.StopSupervisor()
            sessionManager.CleanupAllSessions()

            Application.Shutdown()
            logInfo "Application" "Application shutdown completed"

            0 // return an integer exit code
    with ex ->
        logException "Application" "Fatal error in main application" ex

        try
            Application.Shutdown()
        with _ ->
            ()

        printfn "FATAL ERROR - Check log file: %s" logger.LogPath
        printfn "Error: %s" ex.Message
        1 // return error exit code
