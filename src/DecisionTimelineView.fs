module FCode.DecisionTimelineView

open System
open System.Collections.Concurrent
open Terminal.Gui
open NStack
open FCode.Logger
open FCode.AgentMessaging
open FCode.ColorSchemes
open FCode.UnifiedActivityView

// ===============================================
// 意思決定プロセス可視化型定義
// ===============================================

/// 意思決定段階
type DecisionStage =
    | Problem // 問題発見・提起
    | Analysis // 分析・調査
    | Options // 選択肢抽出
    | Evaluation // 評価・検討
    | Decision // 決定
    | Implementation // 実装・実行
    | Review // レビュー・評価

/// 意思決定エントリ
type DecisionEntry =
    { DecisionId: string // 意思決定一意ID
      Title: string // 意思決定タイトル
      Description: string // 詳細説明
      Stage: DecisionStage // 現在段階
      Priority: MessagePriority // 優先度
      Stakeholders: string list // 関係者リスト
      Timeline: DateTime * DateTime option // 開始時刻・完了時刻
      RelatedTaskIds: string list // 関連タスクID
      Metadata: Map<string, string> // 追加メタデータ
      Status: string } // 状態

/// 意思決定プロセス履歴エントリ
type DecisionHistoryEntry =
    { HistoryId: string // 履歴一意ID
      DecisionId: string // 対象意思決定ID
      Stage: DecisionStage // 段階
      AgentId: string // 実行エージェント
      Action: string // 実行アクション
      Content: string // 内容・結果
      Timestamp: DateTime // 実行時刻
      Metadata: Map<string, string> } // 追加メタデータ

// ===============================================
// 意思決定タイムライン管理
// ===============================================

/// 意思決定タイムライン管理クラス
type DecisionTimelineManager() =
    let decisions = ConcurrentDictionary<string, DecisionEntry>()
    let history = ConcurrentQueue<DecisionHistoryEntry>()
    let maxHistoryEntries = 500 // 最大履歴保持数
    let mutable timelineTextView: TextView option = None

    /// 意思決定一意ID生成
    let generateDecisionId () =
        let timestamp = DateTime.Now.ToString("yyMMdd-HHmmss")
        let guidPart = Guid.NewGuid().ToString("N")[..3]
        $"dec-{timestamp}-{guidPart}"

    /// 履歴エントリ一意ID生成
    let generateHistoryId () =
        let timestamp = DateTime.Now.ToString("HHmmss")
        let guidPart = Guid.NewGuid().ToString("N")[..2]
        $"hist-{timestamp}-{guidPart}"

    /// PMタイムラインTextView設定
    member this.SetTimelineTextView(textView: TextView) =
        timelineTextView <- Some textView
        logInfo "DecisionTimelineView" "PM Timeline TextView set for decision process visualization"

    /// 新規意思決定開始
    member this.StartDecision
        (title: string, description: string, priority: MessagePriority, stakeholders: string list)
        =
        let decisionId = generateDecisionId ()

        let decision =
            { DecisionId = decisionId
              Title = title
              Description = description
              Stage = Problem
              Priority = priority
              Stakeholders = stakeholders
              Timeline = (DateTime.Now, None)
              RelatedTaskIds = []
              Metadata = Map.empty
              Status = "active" }

        decisions.[decisionId] <- decision

        // 履歴追加
        this.AddHistoryEntry(decisionId, Problem, "system", "Start", $"意思決定開始: {title}")

        // UI更新
        this.UpdateTimelineDisplay()

        logInfo "DecisionTimelineView" $"Decision started: {decisionId} - {title}"
        decisionId

    /// 意思決定段階更新
    member this.UpdateDecisionStage(decisionId: string, newStage: DecisionStage, agentId: string, content: string) =
        match decisions.TryGetValue(decisionId) with
        | true, decision ->
            let updatedDecision = { decision with Stage = newStage }
            decisions.[decisionId] <- updatedDecision

            // 履歴追加
            this.AddHistoryEntry(decisionId, newStage, agentId, "StageUpdate", content)

            // UI更新
            this.UpdateTimelineDisplay()

            logInfo "DecisionTimelineView" $"Decision stage updated: {decisionId} - {newStage}"
            true
        | false, _ ->
            logWarning "DecisionTimelineView" $"Decision not found: {decisionId}"
            false

    /// 意思決定完了
    member this.CompleteDecision(decisionId: string, agentId: string, finalDecision: string) =
        match decisions.TryGetValue(decisionId) with
        | true, decision ->
            let completedDecision =
                { decision with
                    Stage = Review
                    Timeline = (fst decision.Timeline, Some DateTime.Now)
                    Status = "completed" }

            decisions.[decisionId] <- completedDecision

            // 履歴追加
            this.AddHistoryEntry(decisionId, Review, agentId, "Complete", $"最終決定: {finalDecision}")

            // UI更新
            this.UpdateTimelineDisplay()

            logInfo "DecisionTimelineView" $"Decision completed: {decisionId} - {finalDecision}"
            true
        | false, _ ->
            logWarning "DecisionTimelineView" $"Decision not found: {decisionId}"
            false

    /// 履歴エントリ追加
    member private this.AddHistoryEntry
        (decisionId: string, stage: DecisionStage, agentId: string, action: string, content: string)
        =
        let historyEntry =
            { HistoryId = generateHistoryId ()
              DecisionId = decisionId
              Stage = stage
              AgentId = agentId
              Action = action
              Content = content
              Timestamp = DateTime.Now
              Metadata = Map.empty }

        history.Enqueue(historyEntry)

        // 最大数超過時の古い履歴削除
        while history.Count > maxHistoryEntries do
            history.TryDequeue() |> ignore

        logDebug "DecisionTimelineView" $"History entry added: {historyEntry.HistoryId} - {action}"

    /// PMタイムライン表示更新
    member private this.UpdateTimelineDisplay() =
        match timelineTextView with
        | Some textView ->
            try
                // アクティブな意思決定と最新履歴を取得・フォーマット
                let activeDecisions =
                    decisions.Values
                    |> Seq.filter (fun d -> d.Status = "active")
                    |> Seq.sortByDescending (fun d -> fst d.Timeline)
                    |> Seq.take (min 5 (Seq.length (decisions.Values)))
                    |> Seq.toArray

                let recentHistory =
                    history.ToArray()
                    |> Array.sortByDescending (fun h -> h.Timestamp)
                    |> Array.take (min 10 history.Count)

                let displayText = this.FormatTimelineForDisplay(activeDecisions, recentHistory)

                // UI更新はメインスレッドで実行
                Application.MainLoop.Invoke(fun () ->
                    textView.Text <- ustring.Make(displayText: string)
                    textView.SetNeedsDisplay())

                logDebug
                    "DecisionTimelineView"
                    $"Timeline display updated with {activeDecisions.Length} active decisions and {recentHistory.Length} history entries"

            with ex ->
                logException "DecisionTimelineView" "Failed to update timeline display" ex
        | None -> logWarning "DecisionTimelineView" "Timeline TextView not set - cannot update display"

    /// タイムライン表示フォーマット
    member private this.FormatTimelineForDisplay
        (activeDecisions: DecisionEntry[], recentHistory: DecisionHistoryEntry[])
        =
        let header = "=== PM 意思決定タイムライン ===\n\n"

        // アクティブな意思決定セクション
        let activeSection =
            if activeDecisions.Length > 0 then
                let activeLines =
                    activeDecisions
                    |> Array.map (fun decision ->
                        let timeStr = (fst decision.Timeline).ToString("MM/dd HH:mm")
                        let stageStr = this.GetStageDisplay(decision.Stage)
                        let priorityStr = this.GetPriorityDisplayForTimeline(decision.Priority)
                        let stakeholdersStr = String.concat "," decision.Stakeholders

                        let titlePreview =
                            if decision.Title.Length > 25 then
                                decision.Title.[..22] + "..."
                            else
                                decision.Title.PadRight(25)

                        $"[{timeStr}] {stageStr} {priorityStr} {titlePreview} ({stakeholdersStr})")
                    |> String.concat "\n"

                $"▼ アクティブ意思決定 ({activeDecisions.Length}件)\n{activeLines}\n\n"
            else
                "▼ アクティブ意思決定なし\n\n"

        // 最新履歴セクション
        let historySection =
            if recentHistory.Length > 0 then
                let historyLines =
                    recentHistory
                    |> Array.map (fun entry ->
                        let timeStr = entry.Timestamp.ToString("HH:mm:ss")
                        let stageStr = this.GetStageDisplay(entry.Stage)
                        let agentStr = entry.AgentId.PadRight(6)
                        let actionStr = entry.Action.PadRight(8)

                        let contentPreview =
                            if entry.Content.Length > 35 then
                                entry.Content.[..32] + "..."
                            else
                                entry.Content

                        $"[{timeStr}] {stageStr} {agentStr} {actionStr} {contentPreview}")
                    |> String.concat "\n"

                $"▼ 最新プロセス履歴 ({recentHistory.Length}件)\n{historyLines}\n\n"
            else
                "▼ プロセス履歴なし\n\n"

        let totalDecisions = decisions.Count

        let footer =
            $"--- 総意思決定数: {totalDecisions} ---\nキーバインド: ESC(終了) Ctrl+X(コマンド) Ctrl+Tab(ペイン切替)"

        header + activeSection + historySection + footer

    /// 意思決定段階表示文字列取得
    member private this.GetStageDisplay(stage: DecisionStage) =
        match stage with
        | Problem -> "🔍 問題"
        | Analysis -> "📊 分析"
        | Options -> "💡 選択肢"
        | Evaluation -> "⚖️ 評価"
        | Decision -> "✅ 決定"
        | Implementation -> "🔧 実装"
        | Review -> "📋 レビュー"

    /// 優先度表示文字列取得（タイムライン用）
    member private this.GetPriorityDisplayForTimeline(priority: MessagePriority) =
        match priority with
        | Critical -> "[🔴]"
        | High -> "[🟡]"
        | Normal -> "[🟢]"
        | Low -> "[⚪]"

    /// AgentMessageから意思決定活動処理
    member this.ProcessDecisionMessage(message: AgentMessage) =
        // エスカレーションメッセージの場合は新規意思決定として処理
        if message.MessageType = MessageType.Escalation then
            let title =
                message.Metadata.TryFind("decision_title")
                |> Option.defaultValue message.Content.[.. min 30 (message.Content.Length - 1)]

            let stakeholders =
                match message.Metadata.TryFind("stakeholders") with
                | Some s -> s.Split(',') |> Array.toList
                | None ->
                    match message.ToAgent with
                    | Some toAgent -> [ message.FromAgent; toAgent ]
                    | None -> [ message.FromAgent ]

            this.StartDecision(title, message.Content, message.Priority, stakeholders)
            |> ignore

        // 意思決定関連メッセージの場合は段階更新として処理
        elif message.MessageType = MessageType.Collaboration then
            match message.Metadata.TryFind("decision_id"), message.Metadata.TryFind("decision_stage") with
            | Some decisionId, Some stageStr ->
                let stage = this.ParseStageFromString(stageStr)

                this.UpdateDecisionStage(decisionId, stage, message.FromAgent, message.Content)
                |> ignore
            | _ -> ()

        logDebug "DecisionTimelineView" $"Processed decision message from {message.FromAgent}: {message.MessageType}"

    /// 文字列から意思決定段階解析
    member private this.ParseStageFromString(stageStr: string) =
        match stageStr.ToLower() with
        | "problem" -> Problem
        | "analysis" -> Analysis
        | "options" -> Options
        | "evaluation" -> Evaluation
        | "decision" -> Decision
        | "implementation" -> Implementation
        | "review" -> Review
        | _ -> Analysis // デフォルト

    /// 指定意思決定の詳細取得
    member this.GetDecisionDetail(decisionId: string) = decisions.TryGetValue(decisionId)

    /// アクティブ意思決定一覧取得
    member this.GetActiveDecisions() =
        decisions.Values |> Seq.filter (fun d -> d.Status = "active") |> Seq.toArray

    /// 全意思決定取得
    member this.GetAllDecisions() = decisions.Values |> Seq.toArray

    /// 意思決定数取得
    member this.GetDecisionCount() = decisions.Count

    /// 履歴クリア
    member this.ClearHistory() =
        history.Clear()
        this.UpdateTimelineDisplay()
        logInfo "DecisionTimelineView" "Decision history cleared"

// ===============================================
// グローバル意思決定タイムライン管理インスタンス
// ===============================================

/// グローバル意思決定タイムライン管理インスタンス
let globalDecisionTimelineManager = DecisionTimelineManager()

/// 新規意思決定開始 (グローバル関数)
let startDecision (title: string) (description: string) (priority: MessagePriority) (stakeholders: string list) =
    globalDecisionTimelineManager.StartDecision(title, description, priority, stakeholders)

/// 意思決定段階更新 (グローバル関数)
let updateDecisionStage (decisionId: string) (newStage: DecisionStage) (agentId: string) (content: string) =
    globalDecisionTimelineManager.UpdateDecisionStage(decisionId, newStage, agentId, content)

/// 意思決定完了 (グローバル関数)
let completeDecision (decisionId: string) (agentId: string) (finalDecision: string) =
    globalDecisionTimelineManager.CompleteDecision(decisionId, agentId, finalDecision)

/// PMタイムラインTextView設定 (グローバル関数)
let setTimelineTextView (textView: TextView) =
    globalDecisionTimelineManager.SetTimelineTextView(textView)

/// AgentMessageから意思決定活動処理 (グローバル関数)
let processDecisionMessage (message: AgentMessage) =
    globalDecisionTimelineManager.ProcessDecisionMessage(message)
