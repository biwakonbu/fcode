module TuiPoC.Program

open System.Threading.Tasks
open Terminal.Gui
open TuiPoC.Logger
open TuiPoC.ColorSchemes
open TuiPoC.KeyBindings
open TuiPoC.ClaudeCodeProcess
open TuiPoC.UIHelpers

[<EntryPoint>]
let main _argv =
    try
        logInfo "Application" "=== fcode TUI Application Starting ==="
        let argsString = System.String.Join(" ", _argv)
        logInfo "Application" $"Command line args: {argsString}"

        // Check if running in CI environment
        let isCI = System.Environment.GetEnvironmentVariable("CI") <> null

        if isCI then
            logInfo "Application" "Running in CI environment - skipping Terminal.Gui initialization"
            0 // Exit successfully in CI
        else
            // Initialize application
            logDebug "Application" "Initializing Terminal.Gui"
            Application.Init()
            logInfo "Application" "Terminal.Gui initialized successfully"

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
                    fv.Add(textView)
                    logInfo "UI" $"TextView added to pane: {title} - Subviews count: {fv.Subviews.Count}"
                    logDebug "UI" $"TextView type: {textView.GetType().Name}"

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
                logDebug "AutoStart" $"Pane {paneId} has {pane.Subviews.Count} subviews"

                let textViews =
                    pane.Subviews
                    |> Seq.mapi (fun i view ->
                        logDebug "AutoStart" $"Subview {i}: {view.GetType().Name}"
                        view)
                    |> Seq.collect findTextViews
                    |> Seq.toList

                logDebug "AutoStart" $"Found {textViews.Length} TextViews in pane: {paneId}"

                match textViews with
                | textView :: _ ->
                    logDebug "AutoStart" $"TextView found for pane: {paneId}"
                    textView.Text <- $"[DEBUG] {paneId}ペイン - TextView発見、Claude Code起動開始..."
                    textView.SetNeedsDisplay()
                    Application.Refresh()

                    let workingDir = System.Environment.CurrentDirectory
                    let success = sessionManager.StartSession(paneId, workingDir, textView)

                    if not success then
                        logError "AutoStart" $"Failed to start Claude Code for pane: {paneId}"
                        textView.Text <- $"[ERROR] {paneId}ペイン - Claude Code起動失敗"
                        textView.SetNeedsDisplay()
                        Application.Refresh()
                    else
                        logInfo "AutoStart" $"Successfully started Claude Code for pane: {paneId}"
                | [] ->
                    // TextViewが見つからない場合のデバッグ情報
                    let debugMsg = $"[ERROR] {paneId}ペイン - TextViewが見つかりません"
                    logError "AutoStart" debugMsg
                    System.Console.WriteLine(debugMsg)

            // 各エージェントペインでClaude Codeを起動 (一時的に無効化 - 安定性のため)
            // logInfo "AutoStart" "Starting Claude Code auto-start process"
            // agentPanes |> List.iter startClaudeCodeForPane
            // logInfo "AutoStart" "Claude Code auto-start process completed"
            logInfo "AutoStart" "Auto-start disabled for stability - use manual start instead"

            // Create focus management for panes
            let focusablePanes = [| convo; dev1; dev2; dev3; qa1; qa2; ux; timeline |]

            // Create Emacs key handler
            let emacsKeyHandler = new EmacsKeyHandler(focusablePanes, sessionManager)

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
                        logInfo "AutoStart" "Executing delayed Claude Code auto-start"

                        // dev1ペインのみで初期テスト
                        let dev1Session = agentPanes |> List.find (fun (id, _) -> id = "dev1")
                        startClaudeCodeForPane dev1Session

                        logInfo "AutoStart" "Delayed auto-start completed for dev1"))
                |> ignore

            // Run application
            logInfo "Application" "Starting TUI application loop"
            setupDelayedAutoStart ()
            Application.Run(top)
            logInfo "Application" "TUI application loop ended"

            // Cleanup
            logInfo "Application" "Cleaning up Claude Code sessions"
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
