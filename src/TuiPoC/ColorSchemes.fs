module TuiPoC.ColorSchemes

open Terminal.Gui

// Use basic Terminal.Gui colors for compatibility
let chatScheme = 
    let scheme = new ColorScheme()
    scheme.Normal <- Terminal.Gui.Attribute.Make(Color.White, Color.Blue)
    scheme.Focus <- Terminal.Gui.Attribute.Make(Color.White, Color.Cyan)
    scheme.HotNormal <- Terminal.Gui.Attribute.Make(Color.Cyan, Color.Blue)
    scheme.HotFocus <- Terminal.Gui.Attribute.Make(Color.Cyan, Color.Cyan)
    scheme

let devScheme = 
    let scheme = new ColorScheme()
    scheme.Normal <- Terminal.Gui.Attribute.Make(Color.Gray, Color.Black) 
    scheme.Focus <- Terminal.Gui.Attribute.Make(Color.White, Color.Gray)
    scheme.HotNormal <- Terminal.Gui.Attribute.Make(Color.Cyan, Color.Black)
    scheme.HotFocus <- Terminal.Gui.Attribute.Make(Color.Cyan, Color.Gray)
    scheme

let qaScheme = 
    let scheme = new ColorScheme()
    scheme.Normal <- Terminal.Gui.Attribute.Make(Color.Brown, Color.Gray)
    scheme.Focus <- Terminal.Gui.Attribute.Make(Color.Brown, Color.White)
    scheme.HotNormal <- Terminal.Gui.Attribute.Make(Color.Red, Color.Gray)
    scheme.HotFocus <- Terminal.Gui.Attribute.Make(Color.Red, Color.White)
    scheme

let uxScheme = 
    let scheme = new ColorScheme()
    scheme.Normal <- Terminal.Gui.Attribute.Make(Color.Cyan, Color.Green)
    scheme.Focus <- Terminal.Gui.Attribute.Make(Color.White, Color.Green)
    scheme.HotNormal <- Terminal.Gui.Attribute.Make(Color.Green, Color.Green)
    scheme.HotFocus <- Terminal.Gui.Attribute.Make(Color.Green, Color.Green)
    scheme

let pmScheme = 
    let scheme = new ColorScheme()
    scheme.Normal <- Terminal.Gui.Attribute.Make(Color.Black, Color.Gray)
    scheme.Focus <- Terminal.Gui.Attribute.Make(Color.Black, Color.White)
    scheme.HotNormal <- Terminal.Gui.Attribute.Make(Color.Blue, Color.Gray)
    scheme.HotFocus <- Terminal.Gui.Attribute.Make(Color.Blue, Color.White)
    scheme

// Apply color scheme to pane based on role
let applySchemeByRole (pane: FrameView) (role: string) =
    match role.ToLower() with
    | "chat" | "会話" -> pane.ColorScheme <- chatScheme
    | "dev1" | "dev2" | "dev3" -> pane.ColorScheme <- devScheme
    | "qa1" | "qa2" -> pane.ColorScheme <- qaScheme
    | "ux" -> pane.ColorScheme <- uxScheme
    | "pm" | "pdm" | "timeline" -> pane.ColorScheme <- pmScheme
    | _ -> pane.ColorScheme <- devScheme // default