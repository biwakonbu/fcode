module FCode.ScrumEventsUI

open System
open Terminal.Gui
open FCode.ScrumEventsManager
open FCode.ColorSchemes
open FCode.Logger

/// 簡素化されたスクラムイベントUI管理
type ScrumEventsUI(scrumEventsManager: ScrumEventsManager) =

    /// スクラムイベント統合ビューを作成（簡素版）
    member this.CreateScrumEventsView(bounds: Rect) =
        let frameView = new FrameView("🏃 スクラムイベント管理")
        frameView.X <- bounds.X
        frameView.Y <- bounds.Y
        frameView.Width <- bounds.Width
        frameView.Height <- bounds.Height

        // スプリント開始ボタン
        let startSprintButton = new Button("新しいスプリント開始")
        startSprintButton.X <- Pos.At(2)
        startSprintButton.Y <- Pos.At(1)

        startSprintButton.add_Clicked (fun _ ->
            async {
                try
                    let! result = scrumEventsManager.StartSprint(1, "基本機能実装スプリント", TimeSpan.FromMinutes(18.0))

                    match result with
                    | Result.Ok sprintId -> logInfo "ScrumEventsUI" <| sprintf "スプリント開始成功: %s" sprintId
                    | Result.Error error -> logError "ScrumEventsUI" <| sprintf "スプリント開始失敗: %A" error
                with ex ->
                    logError "ScrumEventsUI" <| sprintf "スプリント開始例外: %s" ex.Message
            }
            |> Async.Start)

        // デイリースタンドアップボタン
        let dailyButton = new Button("デイリースタンドアップ")
        dailyButton.X <- Pos.At(2)
        dailyButton.Y <- Pos.At(3)

        dailyButton.add_Clicked (fun _ ->
            async {
                try
                    match scrumEventsManager.GetCurrentSprint() with
                    | Some sprint ->
                        let! result = scrumEventsManager.ConductDailyStandUp(sprint.SprintId)

                        match result with
                        | Result.Ok eventId -> logInfo "ScrumEventsUI" <| sprintf "デイリースタンドアップ成功: %s" eventId
                        | Result.Error error -> logError "ScrumEventsUI" <| sprintf "デイリースタンドアップ失敗: %A" error
                    | None -> logWarning "ScrumEventsUI" "アクティブなスプリントがありません"
                with ex ->
                    logError "ScrumEventsUI" <| sprintf "デイリースタンドアップ例外: %s" ex.Message
            }
            |> Async.Start)

        // スプリントレビューボタン
        let reviewButton = new Button("スプリントレビュー")
        reviewButton.X <- Pos.At(2)
        reviewButton.Y <- Pos.At(5)

        reviewButton.add_Clicked (fun _ ->
            async {
                try
                    match scrumEventsManager.GetCurrentSprint() with
                    | Some sprint ->
                        let! result = scrumEventsManager.ConductSprintReview(sprint.SprintId)

                        match result with
                        | Result.Ok eventId -> logInfo "ScrumEventsUI" <| sprintf "スプリントレビュー成功: %s" eventId
                        | Result.Error error -> logError "ScrumEventsUI" <| sprintf "スプリントレビュー失敗: %A" error
                    | None -> logWarning "ScrumEventsUI" "アクティブなスプリントがありません"
                with ex ->
                    logError "ScrumEventsUI" <| sprintf "スプリントレビュー例外: %s" ex.Message
            }
            |> Async.Start)

        // レトロスペクティブボタン
        let retroButton = new Button("レトロスペクティブ")
        retroButton.X <- Pos.At(2)
        retroButton.Y <- Pos.At(7)

        retroButton.add_Clicked (fun _ ->
            async {
                try
                    match scrumEventsManager.GetCurrentSprint() with
                    | Some sprint ->
                        let! result = scrumEventsManager.ConductRetrospective(sprint.SprintId)

                        match result with
                        | Result.Ok eventId -> logInfo "ScrumEventsUI" <| sprintf "レトロスペクティブ成功: %s" eventId
                        | Result.Error error -> logError "ScrumEventsUI" <| sprintf "レトロスペクティブ失敗: %A" error
                    | None -> logWarning "ScrumEventsUI" "アクティブなスプリントがありません"
                with ex ->
                    logError "ScrumEventsUI" <| sprintf "レトロスペクティブ例外: %s" ex.Message
            }
            |> Async.Start)

        // 状況表示テキストビュー
        let statusTextView = new TextView()
        statusTextView.X <- Pos.At(30)
        statusTextView.Y <- Pos.At(1)
        statusTextView.Width <- Dim.Fill(2)
        statusTextView.Height <- Dim.Fill(2)
        statusTextView.ReadOnly <- true
        statusTextView.Text <- NStack.ustring.Make("スクラムイベント管理システム\n\n左のボタンを使用してスクラムイベントを実行してください。")

        frameView.Add(startSprintButton, dailyButton, reviewButton, retroButton, statusTextView)
        frameView.ColorScheme <- defaultScheme

        frameView

    /// 状況表示を更新
    member this.UpdateStatus() =
        match scrumEventsManager.GetCurrentSprint() with
        | Some sprint ->
            let elapsedTime = DateTime.UtcNow - sprint.StartTime
            let remainingTime = sprint.EndTime - DateTime.UtcNow

            let progressPercent =
                (elapsedTime.TotalMinutes / (sprint.EndTime - sprint.StartTime).TotalMinutes)
                * 100.0

            sprintf
                "現在のスプリント: %s\n目標: %s\n進捗: %.1f%%\n残り時間: %.1f分"
                sprint.SprintId
                sprint.Goal
                (max 0.0 (min 100.0 progressPercent))
                (max 0.0 remainingTime.TotalMinutes)
        | None -> "現在アクティブなスプリントはありません。"
