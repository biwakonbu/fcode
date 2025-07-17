module FCode.POWorkflowUI

open System
open Terminal.Gui
open FCode.POWorkflowIntegration
open FCode.Collaboration.CollaborationTypes
open FCode.Logger

/// POワークフローUI統合マネージャー
type POWorkflowUIManager(workflowManager: POWorkflowIntegrationManager) =

    let mutable workflowStatusLabel: Label option = None
    let mutable sprintProgressBar: ProgressBar option = None
    let mutable taskInstructionText: TextView option = None
    let mutable startWorkflowButton: Button option = None
    let mutable stopWorkflowButton: Button option = None
    let mutable workflowResultView: TextView option = None

    /// UI初期化
    member this.InitializeUI(parentView: View) =
        try
            logInfo "POWorkflowUI" "POワークフローUI初期化開始"

            // ワークフロー状態表示ラベル
            let statusLabel = new Label("ワークフロー状態: 待機中")
            statusLabel.X <- Pos.At(2)
            statusLabel.Y <- Pos.At(1)
            statusLabel.Width <- Dim.Fill() - Dim.Sized(2)
            statusLabel.Height <- Dim.Sized(1)
            workflowStatusLabel <- Some statusLabel
            parentView.Add(statusLabel)

            // スプリント進捗バー
            let progressBar = new ProgressBar()
            progressBar.X <- Pos.At(2)
            progressBar.Y <- Pos.At(3)
            progressBar.Width <- Dim.Fill() - Dim.Sized(2)
            progressBar.Height <- Dim.Sized(1)
            progressBar.Fraction <- 0.0f
            sprintProgressBar <- Some progressBar
            parentView.Add(progressBar)

            // タスク指示入力エリア
            let instructionLabel = new Label("PO指示入力:")
            instructionLabel.X <- Pos.At(2)
            instructionLabel.Y <- Pos.At(5)
            instructionLabel.Width <- Dim.Fill() - Dim.Sized(2)
            instructionLabel.Height <- Dim.Sized(1)
            parentView.Add(instructionLabel)

            let instructionText = new TextView()
            instructionText.X <- Pos.At(2)
            instructionText.Y <- Pos.At(6)
            instructionText.Width <- Dim.Fill() - Dim.Sized(2)
            instructionText.Height <- Dim.Sized(4)
            instructionText.WordWrap <- true
            instructionText.Text <- NStack.ustring.Make("ここにPOからの指示を入力してください...")
            taskInstructionText <- Some instructionText
            parentView.Add(instructionText)

            // ワークフロー開始ボタン
            let startButton = new Button("ワークフロー開始")
            startButton.X <- Pos.At(2)
            startButton.Y <- Pos.At(11)
            startButton.Width <- 20
            startButton.Height <- 1
            startButton.add_Clicked (fun _ -> this.OnStartWorkflow())
            startWorkflowButton <- Some startButton
            parentView.Add(startButton)

            // ワークフロー停止ボタン
            let stopButton = new Button("ワークフロー停止")
            stopButton.X <- Pos.At(24)
            stopButton.Y <- Pos.At(11)
            stopButton.Width <- 20
            stopButton.Height <- 1
            stopButton.add_Clicked (fun _ -> this.OnStopWorkflow())
            stopButton.Enabled <- false
            stopWorkflowButton <- Some stopButton
            parentView.Add(stopButton)

            // ワークフロー結果表示エリア
            let resultLabel = new Label("ワークフロー結果:")
            resultLabel.X <- Pos.At(2)
            resultLabel.Y <- Pos.At(13)
            resultLabel.Width <- Dim.Fill() - Dim.Sized(2)
            resultLabel.Height <- Dim.Sized(1)
            parentView.Add(resultLabel)

            let resultView = new TextView()
            resultView.X <- Pos.At(2)
            resultView.Y <- Pos.At(14)
            resultView.Width <- Dim.Fill() - Dim.Sized(2)
            resultView.Height <- Dim.Fill() - Dim.Sized(14)
            resultView.ReadOnly <- true
            resultView.WordWrap <- true
            workflowResultView <- Some resultView
            parentView.Add(resultView)

            // イベントハンドラー登録
            this.RegisterEventHandlers()

            logInfo "POWorkflowUI" "POワークフローUI初期化完了"

        with ex ->
            logError "POWorkflowUI" <| sprintf "UI初期化エラー: %s" ex.Message

    /// イベントハンドラー登録
    member private this.RegisterEventHandlers() =
        // ワークフロー状態変更イベント
        workflowManager.WorkflowStateChanged.Add(fun state ->
            Application.MainLoop.Invoke(fun () ->
                match workflowStatusLabel with
                | Some label ->
                    let stateText = this.GetStateDisplayText(state)
                    label.Text <- NStack.ustring.Make(sprintf "ワークフロー状態: %s" stateText)
                    this.UpdateButtonStates(state)
                | None -> ()))

        // スプリント進捗更新イベント
        workflowManager.SprintProgress.Add(fun progress ->
            Application.MainLoop.Invoke(fun () ->
                match sprintProgressBar with
                | Some progressBar -> progressBar.Fraction <- float32 (progress / 100.0)
                | None -> ()))

        // ワークフロー完了イベント
        workflowManager.WorkflowCompleted.Add(fun result ->
            Application.MainLoop.Invoke(fun () -> this.DisplayWorkflowResult(result)))

    /// ワークフロー状態の表示テキストを取得
    member private this.GetStateDisplayText(state: POWorkflowState) : string =
        match state with
        | POWorkflowState.Idle -> "待機中"
        | ReceivingInstruction -> "指示受信中"
        | ProcessingTask -> "タスク処理中"
        | DecomposingTask -> "タスク分解中"
        | AssigningToAgents -> "エージェント配分中"
        | ExecutingTasks -> "タスク実行中"
        | QualityGateCheck -> "品質ゲートチェック中"
        | POWorkflowState.Completed -> "完了"
        | POWorkflowState.Failed error -> sprintf "失敗: %s" error

    /// ボタンの状態を更新
    member private this.UpdateButtonStates(state: POWorkflowState) =
        match startWorkflowButton, stopWorkflowButton with
        | Some startBtn, Some stopBtn ->
            match state with
            | POWorkflowState.Idle
            | POWorkflowState.Completed
            | POWorkflowState.Failed _ ->
                startBtn.Enabled <- true
                stopBtn.Enabled <- false
            | _ ->
                startBtn.Enabled <- false
                stopBtn.Enabled <- true
        | _ -> ()

    /// ワークフロー開始処理
    member private this.OnStartWorkflow() =
        async {
            try
                logInfo "POWorkflowUI" "ワークフロー開始ボタンクリック"

                match taskInstructionText with
                | Some textView ->
                    let instruction = textView.Text.ToString().Trim()

                    if String.IsNullOrEmpty(instruction) || instruction = "ここにPOからの指示を入力してください..." then
                        MessageBox.ErrorQuery("エラー", "指示を入力してください", "OK") |> ignore
                    else
                        let! result = workflowManager.StartWorkflow(instruction) |> Async.AwaitTask

                        match result with
                        | Result.Ok workflowId ->
                            logInfo "POWorkflowUI" <| sprintf "ワークフロー開始成功: %s" workflowId
                            this.UpdateResultView(sprintf "ワークフロー開始: %s\n指示: %s\n" workflowId instruction)
                        | Result.Error error ->
                            logError "POWorkflowUI" <| sprintf "ワークフロー開始失敗: %A" error
                            let errorMsg = error.ToUserMessage().UserMessage

                            MessageBox.ErrorQuery("エラー", sprintf "ワークフロー開始に失敗しました: %s" errorMsg, "OK")
                            |> ignore
                | None -> MessageBox.ErrorQuery("エラー", "テキストエリアが初期化されていません", "OK") |> ignore

            with ex ->
                logError "POWorkflowUI" <| sprintf "ワークフロー開始エラー: %s" ex.Message

                MessageBox.ErrorQuery("エラー", sprintf "エラーが発生しました: %s" ex.Message, "OK")
                |> ignore
        }
        |> Async.Start

    /// ワークフロー停止処理
    member private this.OnStopWorkflow() =
        async {
            try
                logInfo "POWorkflowUI" "ワークフロー停止ボタンクリック"

                let! result = workflowManager.StopWorkflow() |> Async.AwaitTask

                match result with
                | Result.Ok _ ->
                    logInfo "POWorkflowUI" "ワークフロー停止成功"
                    this.UpdateResultView("ワークフロー停止\n")
                | Result.Error error ->
                    logError "POWorkflowUI" <| sprintf "ワークフロー停止失敗: %A" error
                    let errorMsg = error.ToUserMessage().UserMessage

                    MessageBox.ErrorQuery("エラー", sprintf "ワークフロー停止に失敗しました: %s" errorMsg, "OK")
                    |> ignore

            with ex ->
                logError "POWorkflowUI" <| sprintf "ワークフロー停止エラー: %s" ex.Message

                MessageBox.ErrorQuery("エラー", sprintf "エラーが発生しました: %s" ex.Message, "OK")
                |> ignore
        }
        |> Async.Start

    /// ワークフロー結果を表示
    member private this.DisplayWorkflowResult(result: POWorkflowResult) =
        try
            let resultText =
                sprintf "=== ワークフロー完了 ===\n"
                + sprintf "ワークフローID: %s\n" result.WorkflowId
                + sprintf "開始時刻: %s\n" (result.ExecutionStartTime.ToString("yyyy-MM-dd HH:mm:ss"))
                + sprintf
                    "終了時刻: %s\n"
                    (result.ExecutionEndTime
                     |> Option.map (fun t -> t.ToString("yyyy-MM-dd HH:mm:ss"))
                     |> Option.defaultValue "未完了")
                + sprintf
                    "実行時間: %s\n"
                    (result.Duration
                     |> Option.map (fun d -> d.ToString(@"mm\:ss"))
                     |> Option.defaultValue "不明")
                + sprintf "最終状態: %s\n" (this.GetStateDisplayText(result.FinalState))
                + (match result.QualityGateResult with
                   | Some qr -> sprintf "品質スコア: %.1f (閾値クリア: %b)\n" qr.OverallScore qr.PassesThreshold
                   | None -> "品質評価: 未実行\n")
                + sprintf "配分エージェント: %s\n" (String.Join(", ", result.AssignedAgents))
                + "\n"

            this.UpdateResultView(resultText)

        with ex ->
            logError "POWorkflowUI" <| sprintf "結果表示エラー: %s" ex.Message

    /// 結果表示エリアを更新
    member private this.UpdateResultView(text: string) =
        match workflowResultView with
        | Some resultView ->
            let currentText = resultView.Text.ToString()
            let newText = currentText + text
            resultView.Text <- NStack.ustring.Make(newText)
            resultView.MoveEnd()
        | None -> ()

    /// UI状態をリセット
    member this.ResetUI() =
        try
            match workflowStatusLabel with
            | Some label -> label.Text <- NStack.ustring.Make("ワークフロー状態: 待機中")
            | None -> ()

            match sprintProgressBar with
            | Some progressBar -> progressBar.Fraction <- 0.0f
            | None -> ()

            match taskInstructionText with
            | Some textView -> textView.Text <- NStack.ustring.Make("ここにPOからの指示を入力してください...")
            | None -> ()

            match workflowResultView with
            | Some resultView -> resultView.Text <- NStack.ustring.Make("")
            | None -> ()

            this.UpdateButtonStates(POWorkflowState.Idle)

        with ex ->
            logError "POWorkflowUI" <| sprintf "UIリセットエラー: %s" ex.Message

    /// リソースクリーンアップ
    interface IDisposable with
        member this.Dispose() =
            logInfo "POWorkflowUI" "POワークフローUIリソースクリーンアップ完了"
