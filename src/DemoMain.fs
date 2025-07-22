module FCode.DemoMain

open System
open FCode.Logger
open FCode.DemoIntegrationMain
open FCode.CollaborationDemoTest

/// FC-036 デモ実行専用エントリーポイント
/// プロジェクト統合前の独立検証用
let runDemoMain args =
    try
        logInfo "DemoMain" "FC-036: エージェント協調機能動作実証開始"

        match args with
        | [||] ->
            // 引数なし: 包括的受け入れテスト実行
            printfn "FC-036: エージェント協調機能動作実証 - 包括的受け入れテスト"

            let testResult =
                CollaborationDemoTestRunner.runCLIAcceptanceTest () |> Async.RunSynchronously

            if testResult then 0 else 1

        | [| "ui" |] ->
            // UI実証モード
            printfn "FC-036: UI統合デモ起動中..."
            DemoRunner.runDemoUI ()
            0

        | [| demoType |] when [ "po"; "scrum"; "complete" ] |> List.contains (demoType.ToLower()) ->
            // 自動デモ実行
            printfn "FC-036: 自動デモ実行 - %s" demoType
            DemoRunner.runAutomatedDemo (demoType) |> Async.RunSynchronously
            0

        | _ ->
            printfn "FC-036: エージェント協調機能動作実証"
            printfn ""
            printfn "使用方法:"
            printfn "  fcode-demo              # 包括的受け入れテスト実行"
            printfn "  fcode-demo ui           # UI統合デモ起動"
            printfn "  fcode-demo po           # PO指示→実行フローデモ"
            printfn "  fcode-demo scrum        # スクラムイベント実証デモ"
            printfn "  fcode-demo complete     # 包括的デモ実行"
            printfn ""
            printfn "受け入れ基準:"
            printfn "  1. PO指示からタスク完了まで完全フロー動作確認"
            printfn "  2. エージェント状態同期・競合制御機能実証"
            printfn "  3. 18分スプリント・スクラムイベント完全実行"
            1

    with ex ->
        logError "DemoMain" <| sprintf "FC-036デモ実行エラー: %s" ex.Message
        printfn "❌ FC-036デモ実行エラー: %s" ex.Message
        1
