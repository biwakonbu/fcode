module TuiPoC.ColorSchemes

open Terminal.Gui

// Use terminal default colors for all schemes
let defaultScheme =
    let scheme = new ColorScheme()
    scheme.Normal <- Terminal.Gui.Attribute.Make(Color.White, Color.Black)
    scheme.Focus <- Terminal.Gui.Attribute.Make(Color.Black, Color.Gray)
    scheme.HotNormal <- Terminal.Gui.Attribute.Make(Color.Cyan, Color.Black)
    scheme.HotFocus <- Terminal.Gui.Attribute.Make(Color.Cyan, Color.Gray)
    scheme

let chatScheme = defaultScheme
let devScheme = defaultScheme
let qaScheme = defaultScheme
let uxScheme = defaultScheme
let pmScheme = defaultScheme

// Apply color scheme to pane based on role
let applySchemeByRole (pane: FrameView) (role: string) =
    match role.ToLower() with
    | "chat"
    | "会話" -> pane.ColorScheme <- chatScheme
    | "dev1"
    | "dev2"
    | "dev3" -> pane.ColorScheme <- devScheme
    | "qa1"
    | "qa2" -> pane.ColorScheme <- qaScheme
    | "ux" -> pane.ColorScheme <- uxScheme
    | "pm"
    | "pdm"
    | "timeline" -> pane.ColorScheme <- pmScheme
    | _ -> pane.ColorScheme <- devScheme // default
