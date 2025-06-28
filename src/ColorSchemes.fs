module FCode.ColorSchemes

open Terminal.Gui

// Use terminal default colors for all schemes
let defaultScheme =
    let scheme = ColorScheme()
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

// テスト可能なビューのインターフェース（テスト用）
type ITestableView =
    abstract ColorScheme: ColorScheme with get, set
    abstract Title: string

// カラースキーム選択ロジック（UI非依存）
let getSchemeByRole (role: string) =
    match role.ToLower() with
    | "chat"
    | "会話" -> chatScheme
    | "dev1"
    | "dev2"
    | "dev3" -> devScheme
    | "qa1"
    | "qa2" -> qaScheme
    | "ux" -> uxScheme
    | "pm"
    | "pdm"
    | "timeline" -> pmScheme
    | _ -> devScheme // default

// テスト可能なカラースキーム適用（UI非依存）
let applySchemeByRoleTestable (view: ITestableView) (role: string) =
    view.ColorScheme <- getSchemeByRole role

// Apply color scheme to pane based on role (既存のTerminal.Gui用)
let applySchemeByRole (pane: FrameView) (role: string) =
    pane.ColorScheme <- getSchemeByRole role
