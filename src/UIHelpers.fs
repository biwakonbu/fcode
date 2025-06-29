module FCode.UIHelpers

open Terminal.Gui
open FCode.Logger

/// UI階層構造をデバッグ出力する関数
let rec dumpViewHierarchy (view: View) (indent: int) =
    let indentStr = String.replicate indent "  "
    let viewType = view.GetType().Name
    let viewId = view.Id
    let subviewCount = view.Subviews.Count

    logDebug "UIStructure" $"{indentStr}{viewType} (ID: {viewId}, Subviews: {subviewCount})"

    // 特別なプロパティがあれば表示
    match view with
    | :? FrameView as fv ->
        logDebug "UIStructure" $"{indentStr}  FrameView Title: {fv.Title}"
        // FrameViewのContentViewプロパティを確認
        try
            let contentView = fv.GetType().GetProperty("ContentView")

            if contentView <> null then
                let cv = contentView.GetValue(fv)

                if cv <> null then
                    logDebug "UIStructure" $"{indentStr}  Has ContentView: {cv.GetType().Name}"
        with _ ->
            ()
    | :? TextView as tv -> logDebug "UIStructure" $"{indentStr}  TextView ReadOnly: {tv.ReadOnly}"
    | _ -> ()

    // 子要素を再帰的にダンプ
    view.Subviews
    |> Seq.iteri (fun i subview ->
        logDebug "UIStructure" $"{indentStr}[{i}]:"
        dumpViewHierarchy subview (indent + 1))

/// TextView階層検索関数 - Terminal.Gui 1.15.0対応の完全検索
let rec findTextViews (view: View) =
    seq {
        // 直接TextViewの場合
        match view with
        | :? TextView as tv ->
            logDebug "UISearch" $"Found TextView: {tv.GetType().Name} (ReadOnly: {tv.ReadOnly})"
            yield tv
        | _ -> ()

        // 子要素も再帰的に検索
        for subview in view.Subviews do
            yield! findTextViews subview

        // FrameViewの場合、Terminal.Gui 1.15.0の内部構造を調査
        match view with
        | :? FrameView as fv ->
            // ContentViewプロパティの確認
            try
                let contentViewProp = fv.GetType().GetProperty("ContentView")

                if contentViewProp <> null then
                    let contentView = contentViewProp.GetValue(fv)

                    if contentView <> null && contentView :? View then
                        logDebug "UISearch" "Searching FrameView.ContentView"
                        yield! findTextViews (contentView :?> View)
            with ex ->
                logDebug "UISearch" $"ContentView access failed: {ex.Message}"

            // Border内のコンテンツエリアも確認
            try
                let borderProp = fv.GetType().GetProperty("Border")

                if borderProp <> null then
                    let border = borderProp.GetValue(fv)

                    if border <> null then
                        logDebug "UISearch" $"FrameView has Border: {border.GetType().Name}"
            with _ ->
                ()

            // GetContentSizeメソッドなどの確認
            try
                let methods = fv.GetType().GetMethods() |> Array.map (fun m -> m.Name)
                let methodList = String.concat ", " methods
                logDebug "UISearch" $"FrameView methods: {methodList}"
            with _ ->
                ()

        | _ -> ()
    }

/// FrameView内の全TextViewを取得する便利関数
let getTextViewsFromPane (pane: FrameView) =
    logInfo "UISearch" $"Starting TextView search for pane: {pane.Title}"
    dumpViewHierarchy pane 0
    let textViews = pane.Subviews |> Seq.collect findTextViews |> Seq.toList
    logInfo "UISearch" $"Found {textViews.Length} TextViews in pane: {pane.Title}"
    textViews
