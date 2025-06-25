module TuiPoC.Program

open Terminal.Gui
open TuiPoC.ColorSchemes

[<EntryPoint>]
let main _argv =
    // Initialize application
    Application.Init()

    let top = Application.Top

    // --- Conversation Pane -------------------------------------------------
    let conversationWidth = 20 // columns  
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

    // ----------------------------------------------------------------------
    // Right-hand container – holds all other panes
    let right = new FrameView()
    right.X <- Pos.Right convo
    right.Y <- 0
    right.Width <- Dim.Fill()
    right.Height <- Dim.Fill()
    // invisible wrapper - no border

    // Helper function to create a pane with a given title
    let makePane title =
        let fv = new FrameView(title: string)
        fv.Border.Effect3D <- false
        // Apply color scheme based on title
        applySchemeByRole fv title
        fv

    // Row heights (percentage of right-hand container)
    let devRowHeight   = Dim.Percent 40.0f // 上段: dev1-3
    let qaRowHeight    = Dim.Percent 40.0f // 中段: qa1-2, ux

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

    // Create focus management for panes
    let focusablePanes = [| convo; dev1; dev2; dev3; qa1; qa2; ux; timeline |]
    let mutable currentFocusIndex = 0

    // Add key handling for focus navigation
    let keyHandler = System.Action<View.KeyEventEventArgs>(fun args ->
        match args.KeyEvent.Key with
        | Key.Tab when args.KeyEvent.IsCtrl ->
            // Ctrl+Tab: cycle through panes
            currentFocusIndex <- (currentFocusIndex + 1) % focusablePanes.Length
            focusablePanes.[currentFocusIndex].SetFocus()
            args.Handled <- true
        | Key.C when args.KeyEvent.IsCtrl ->
            // Ctrl+C: toggle conversation pane visibility  
            convo.Visible <- not convo.Visible
            args.Handled <- true
        | _ -> ())

    // Override key processing
    top.add_KeyDown keyHandler

    // Set initial focus 
    focusablePanes.[0].SetFocus()

    // Run application
    Application.Run()
    Application.Shutdown()
    0 // return an integer exit code 
