module FCode.RealtimeWorkflowUI

open System
open System.Collections.Generic
open Terminal.Gui
open FCode.Logger
open FCode.POWorkflowEnhanced
//open FCode.UIHelpers
//open FCode.ColorSchemes

/// FC-030: リアルタイムワークフローUI実装
/// 全ペイン（dev1-3, qa1-2, ux, pm）でのリアルタイム表示統合
type RealtimeWorkflowUIManager() =
    
    let mutable workflowManager: POWorkflowEnhancedManager option = None
    let mutable isInitialized = false
    
    // UI コンポーネント管理
    let uiComponents = Dictionary<string, View>()
    let statusLabels = Dictionary<string, Label>()
    let progressBars = Dictionary<string, ProgressBar>()
    let taskViews = Dictionary<string, TextView>()
    
    /// 初期化
    member this.Initialize(wfManager: POWorkflowEnhancedManager, panes: Map<string, View>) =
        try
            workflowManager <- Some wfManager
            
            // 各ペインにUIコンポーネント設定
            panes |> Map.iter (fun paneId paneView ->
                this.SetupPaneUI(paneId, paneView)
            )
            
            // ワークフローイベント購読
            wfManager.WorkflowStarted.Add(this.OnWorkflowStarted)
            wfManager.WorkflowProgress.Add(this.OnWorkflowProgress)
            wfManager.WorkflowCompleted.Add(this.OnWorkflowCompleted)
            wfManager.AgentStatusUpdate.Add(this.OnAgentStatusUpdate)
            
            isInitialized <- true
            Logger.log LogLevel.Info "RealtimeWorkflowUI" "リアルタイムワークフローUI初期化完了"
            
        with ex ->
            Logger.log LogLevel.Error "RealtimeWorkflowUI" $"初期化エラー: {ex.Message}"
    
    /// ペイン別UI設定
    member private this.SetupPaneUI(paneId: string, paneView: View) =
        try
            // ペインタイプ判定
            let paneType = this.GetPaneType(paneId)
            
            match paneType with
            | "conversation" ->
                this.SetupConversationPane(paneId, paneView)
            | "dev" ->
                this.SetupDeveloperPane(paneId, paneView)
            | "qa" ->
                this.SetupQAPane(paneId, paneView)
            | "ux" ->
                this.SetupUXPane(paneId, paneView)
            | "pm" ->
                this.SetupPMPane(paneId, paneView)
            | _ ->
                Logger.log LogLevel.Warning "RealtimeWorkflowUI" $"未知のペインタイプ: {paneId}"
                
        with ex ->
            Logger.log LogLevel.Error "RealtimeWorkflowUI" $"ペインUI設定エラー ({paneId}): {ex.Message}"
    
    /// ペインタイプ判定
    member private this.GetPaneType(paneId: string) : string =
        if paneId.Contains("conversation") then "conversation"
        elif paneId.Contains("dev") then "dev"
        elif paneId.Contains("qa") then "qa"
        elif paneId.Contains("ux") then "ux"
        elif paneId.Contains("pm") then "pm"
        else "unknown"
    
    /// 会話ペイン設定
    member private this.SetupConversationPane(paneId: string, paneView: View) =
        try
            // PO指示入力エリア
            let instructionLabel = new Label("PO指示入力:")
            instructionLabel.X <- Pos.At(1)
            instructionLabel.Y <- Pos.At(1)
            paneView.Add(instructionLabel)
            
            let instructionText = new TextView()
            instructionText.X <- Pos.At(1)
            instructionText.Y <- Pos.At(2)
            instructionText.Width <- Dim.Fill() - Dim.Sized(2)
            instructionText.Height <- Dim.Sized(4)
            instructionText.Text <- "タスクを入力してください"
            paneView.Add(instructionText)
            
            // スプリント開始ボタン
            let startButton = new Button("18分スプリント開始")
            startButton.X <- Pos.At(1)
            startButton.Y <- Pos.At(7)
            startButton.Clicked.Add(fun _ -> this.StartWorkflowFromInput(instructionText.Text.ToString()))
            paneView.Add(startButton)
            
            // 進捗表示
            let progressLabel = new Label("スプリント進捗: 待機中")
            progressLabel.X <- Pos.At(1)
            progressLabel.Y <- Pos.At(9)
            statusLabels.[paneId] <- progressLabel
            paneView.Add(progressLabel)
            
            let progressBar = new ProgressBar()
            progressBar.X <- Pos.At(1)
            progressBar.Y <- Pos.At(10)
            progressBar.Width <- Dim.Fill() - Dim.Sized(2)
            progressBar.Height <- Dim.Sized(1)
            progressBar.Fraction <- 0.0f
            progressBars.[paneId] <- progressBar
            paneView.Add(progressBar)
            
            // ワークフロー結果表示エリア
            let resultView = new TextView()
            resultView.X <- Pos.At(1)
            resultView.Y <- Pos.At(12)
            resultView.Width <- Dim.Fill() - Dim.Sized(2)
            resultView.Height <- Dim.Fill() - Dim.Sized(13)
            resultView.ReadOnly <- true
            taskViews.[paneId] <- resultView
            paneView.Add(resultView)
            
        with ex ->
            Logger.log LogLevel.Error "RealtimeWorkflowUI" $"会話ペイン設定エラー: {ex.Message}"
    
    /// 開発者ペイン設定
    member private this.SetupDeveloperPane(paneId: string, paneView: View) =
        try
            // エージェント状態表示
            let agentLabel = new Label($"{paneId.ToUpper()} 状態: 待機中")
            agentLabel.X <- Pos.At(1)
            agentLabel.Y <- Pos.At(1)
            statusLabels.[paneId] <- agentLabel
            paneView.Add(agentLabel)
            
            // タスク進捗バー
            let taskProgressBar = new ProgressBar()
            taskProgressBar.X <- Pos.At(1)
            taskProgressBar.Y <- Pos.At(2)
            taskProgressBar.Width <- Dim.Fill() - Dim.Sized(2)
            taskProgressBar.Height <- Dim.Sized(1)
            taskProgressBar.Fraction <- 0.0f
            progressBars.[paneId] <- taskProgressBar
            paneView.Add(taskProgressBar)
            
            // 現在のタスク表示
            let currentTaskView = new TextView()
            currentTaskView.X <- Pos.At(1)
            currentTaskView.Y <- Pos.At(4)
            currentTaskView.Width <- Dim.Fill() - Dim.Sized(2)
            currentTaskView.Height <- Dim.Sized(6)
            currentTaskView.ReadOnly <- true
            currentTaskView.Text <- "タスク待機中..."
            taskViews.[paneId] <- currentTaskView
            paneView.Add(currentTaskView)
            
        with ex ->
            Logger.log LogLevel.Error "RealtimeWorkflowUI" $"開発者ペイン設定エラー ({paneId}): {ex.Message}"
    
    /// QAペイン設定
    member private this.SetupQAPane(paneId: string, paneView: View) =
        try
            // QA状態表示
            let qaLabel = new Label($"{paneId.ToUpper()} 状態: 待機中")
            qaLabel.X <- Pos.At(1)
            qaLabel.Y <- Pos.At(1)
            statusLabels.[paneId] <- qaLabel
            paneView.Add(qaLabel)
            
            // テスト進捗バー
            let testProgressBar = new ProgressBar()
            testProgressBar.X <- Pos.At(1)
            testProgressBar.Y <- Pos.At(2)
            testProgressBar.Width <- Dim.Fill() - Dim.Sized(2)
            testProgressBar.Height <- Dim.Sized(1)
            testProgressBar.Fraction <- 0.0f
            progressBars.[paneId] <- testProgressBar
            paneView.Add(testProgressBar)
            
            // テスト結果表示
            let testResultView = new TextView()
            testResultView.X <- Pos.At(1)
            testResultView.Y <- Pos.At(4)
            testResultView.Width <- Dim.Fill() - Dim.Sized(2)
            testResultView.Height <- Dim.Sized(8)
            testResultView.ReadOnly <- true
            testResultView.Text <- "テスト実行待機中..."
            taskViews.[paneId] <- testResultView
            paneView.Add(testResultView)
            
        with ex ->
            Logger.log LogLevel.Error "RealtimeWorkflowUI" $"QAペイン設定エラー ({paneId}): {ex.Message}"
    
    /// UXペイン設定
    member private this.SetupUXPane(paneId: string, paneView: View) =
        try
            // UX状態表示
            let uxLabel = new Label("UX 状態: 待機中")
            uxLabel.X <- Pos.At(1)
            uxLabel.Y <- Pos.At(1)
            statusLabels.[paneId] <- uxLabel
            paneView.Add(uxLabel)
            
            // デザイン進捗バー
            let designProgressBar = new ProgressBar()
            designProgressBar.X <- Pos.At(1)
            designProgressBar.Y <- Pos.At(2)
            designProgressBar.Width <- Dim.Fill() - Dim.Sized(2)
            designProgressBar.Height <- Dim.Sized(1)
            designProgressBar.Fraction <- 0.0f
            progressBars.[paneId] <- designProgressBar
            paneView.Add(designProgressBar)
            
            // UI/UXデザイン表示
            let designView = new TextView()
            designView.X <- Pos.At(1)
            designView.Y <- Pos.At(4)
            designView.Width <- Dim.Fill() - Dim.Sized(2)
            designView.Height <- Dim.Sized(8)
            designView.ReadOnly <- true
            designView.Text <- "UI/UXデザイン待機中..."
            taskViews.[paneId] <- designView
            paneView.Add(designView)
            
        with ex ->
            Logger.log LogLevel.Error "RealtimeWorkflowUI" $"UXペイン設定エラー: {ex.Message}"
    
    /// PMペイン設定
    member private this.SetupPMPane(paneId: string, paneView: View) =
        try
            // PM状態表示
            let pmLabel = new Label("PM 状態: 待機中")
            pmLabel.X <- Pos.At(1)
            pmLabel.Y <- Pos.At(1)
            statusLabels.[paneId] <- pmLabel
            paneView.Add(pmLabel)
            
            // スプリント全体進捗
            let sprintProgressBar = new ProgressBar()
            sprintProgressBar.X <- Pos.At(1)
            sprintProgressBar.Y <- Pos.At(2)
            sprintProgressBar.Width <- Dim.Fill() - Dim.Sized(2)
            sprintProgressBar.Height <- Dim.Sized(1)
            sprintProgressBar.Fraction <- 0.0f
            progressBars.[paneId] <- sprintProgressBar
            paneView.Add(sprintProgressBar)
            
            // チーム状況表示
            let teamStatusView = new TextView()
            teamStatusView.X <- Pos.At(1)
            teamStatusView.Y <- Pos.At(4)
            teamStatusView.Width <- Dim.Fill() - Dim.Sized(2)
            teamStatusView.Height <- Dim.Sized(8)
            teamStatusView.ReadOnly <- true
            teamStatusView.Text <- "チーム状況: 全エージェント待機中"
            taskViews.[paneId] <- teamStatusView
            paneView.Add(teamStatusView)
            
        with ex ->
            Logger.log LogLevel.Error "RealtimeWorkflowUI" $"PMペイン設定エラー: {ex.Message}"
    
    /// ワークフロー開始（入力から）
    member private this.StartWorkflowFromInput(instruction: string) =
        try
            match workflowManager with
            | Some manager ->
                if String.IsNullOrWhiteSpace(instruction) then
                    this.ShowMessage("エラー", "指示を入力してください")
                else
                    match manager.StartSprintWorkflow(instruction.Trim()) with
                    | Ok sprintInfo ->
                        this.ShowMessage("成功", $"18分スプリント開始: {sprintInfo.SprintId}")
                    | Error msg ->
                        this.ShowMessage("エラー", $"スプリント開始失敗: {msg}")
            | None ->
                this.ShowMessage("エラー", "ワークフローマネージャーが初期化されていません")
                
        with ex ->
            Logger.log LogLevel.Error "RealtimeWorkflowUI" $"ワークフロー開始エラー: {ex.Message}"
            this.ShowMessage("エラー", $"予期しないエラー: {ex.Message}")
    
    /// ワークフロー開始イベントハンドラ
    member private this.OnWorkflowStarted(sprintInfo: SprintInfo) =
        try
            // 会話ペインの進捗更新
            if statusLabels.ContainsKey("conversation") then
                statusLabels.["conversation"].Text <- $"スプリント実行中: {sprintInfo.SprintId}"
            
            Logger.log LogLevel.Info "RealtimeWorkflowUI" $"ワークフロー開始UI更新完了: {sprintInfo.SprintId}"
            
        with ex ->
            Logger.log LogLevel.Error "RealtimeWorkflowUI" $"ワークフロー開始UI更新エラー: {ex.Message}"
    
    /// ワークフロー進捗イベントハンドラ
    member private this.OnWorkflowProgress(progress: WorkflowProgress) =
        try
            // 全体進捗の更新
            let progressFraction = float32 progress.ElapsedMinutes / float32 progress.TotalMinutes
            
            if progressBars.ContainsKey("conversation") then
                progressBars.["conversation"].Fraction <- progressFraction
            
            if progressBars.ContainsKey("pm") then
                progressBars.["pm"].Fraction <- progressFraction
            
        with ex ->
            Logger.log LogLevel.Error "RealtimeWorkflowUI" $"ワークフロー進捗UI更新エラー: {ex.Message}"
    
    /// ワークフロー完了イベントハンドラ
    member private this.OnWorkflowCompleted(result: WorkflowResult) =
        try
            // 結果表示の更新
            if statusLabels.ContainsKey("conversation") then
                statusLabels.["conversation"].Text <- $"スプリント完了: {result.Status}"
            
            if taskViews.ContainsKey("conversation") then
                let resultText = sprintf "スプリント結果:\nID: %s\n指示: %s\n状態: %A\n品質スコア: %.1f%%\n" 
                                        result.SprintId result.Instruction result.Status result.QualityScore
                taskViews.["conversation"].Text <- resultText
            
            Logger.log LogLevel.Info "RealtimeWorkflowUI" $"ワークフロー完了UI更新完了: {result.SprintId}"
            
        with ex ->
            Logger.log LogLevel.Error "RealtimeWorkflowUI" $"ワークフロー完了UI更新エラー: {ex.Message}"
    
    /// エージェント状況更新イベントハンドラ
    member private this.OnAgentStatusUpdate(update: AgentStatusUpdate) =
        try
            let paneId = update.AgentId
            
            // ステータスラベル更新
            if statusLabels.ContainsKey(paneId) then
                statusLabels.[paneId].Text <- sprintf "%s 状態: %A" (update.AgentId.ToUpper()) update.Status
            
            // 進捗バー更新
            if progressBars.ContainsKey(paneId) then
                progressBars.[paneId].Fraction <- float32 update.Progress
            
        with ex ->
            Logger.log LogLevel.Error "RealtimeWorkflowUI" $"エージェント状況更新UI反映エラー: {ex.Message}"
    
    /// メッセージ表示
    member private this.ShowMessage(title: string, message: string) =
        try
            MessageBox.Query(title, message, "OK") |> ignore
        with ex ->
            Logger.log LogLevel.Error "RealtimeWorkflowUI" $"メッセージ表示エラー: {ex.Message}"
    
    /// リソース解放
    interface IDisposable with
        member this.Dispose() =
            uiComponents.Clear()
            statusLabels.Clear()
            progressBars.Clear()
            taskViews.Clear()
            Logger.log LogLevel.Info "RealtimeWorkflowUI" "リアルタイムワークフローUIリソース解放完了"