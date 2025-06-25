module TuiPoC.UIHelpers

open Terminal.Gui

/// TextView階層検索関数 - FrameView内のContentView階層も含めて再帰的に検索
let rec findTextViews (view: View) =
    seq {
        // 直接TextViewの場合
        match view with
        | :? TextView as tv -> yield tv
        | _ -> ()
        
        // 子要素も再帰的に検索
        for subview in view.Subviews do
            yield! findTextViews subview
    }

/// FrameView内の全TextViewを取得する便利関数
let getTextViewsFromPane (pane: FrameView) =
    pane.Subviews 
    |> Seq.collect findTextViews
    |> Seq.toList