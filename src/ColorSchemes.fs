module FCode.ColorSchemes

open Terminal.Gui

// CI環境判定
let isCI =
    not (System.String.IsNullOrEmpty(System.Environment.GetEnvironmentVariable("CI")))

// Use terminal default colors for all schemes
let defaultScheme =
    if isCI then
        // CI環境では制御文字出力を避けるため、null ColorSchemeを使用
        null
    else
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
    if not isCI then
        view.ColorScheme <- getSchemeByRole role

// Apply color scheme to pane based on role (既存のTerminal.Gui用)
let applySchemeByRole (pane: FrameView) (role: string) =
    if not isCI then
        pane.ColorScheme <- getSchemeByRole role
