module FCode.QualityGateUI

open System
open System.Text
open Terminal.Gui
open FCode.QualityGateManager
open FCode.TaskAssignmentManager
open FCode.Collaboration.CollaborationTypes
open FCode.Logger
open FCode.ColorSchemes

/// 品質ゲート表示状態
type QualityGateDisplayState =
    | Idle
    | Evaluating
    | DisplayingResults
    | RequiresAction

/// 品質ゲートUI統合情報
type QualityGateUIInfo =
    { TaskId: string
      TaskTitle: string
      EvaluationResult: QualityEvaluationResult option
      ReviewResult: ReviewResult option
      AlternativeProposals: AlternativeProposal list option
      DisplayState: QualityGateDisplayState
      LastUpdated: DateTime }

/// 品質ゲートUI管理クラス
type QualityGateUIManager(qualityGateManager: QualityGateManager) =

    let mutable currentDisplayInfo: QualityGateUIInfo option = None
    let mutable uiComponents: Map<string, View> = Map.empty

    /// 品質ゲート評価結果をUI表示用にフォーマット
    member this.FormatQualityEvaluationResult(result: QualityEvaluationResult) : string =
        let sb = StringBuilder()

        sb.AppendFormat("🎯 品質評価結果 - {0}\n\n", result.TaskId) |> ignore

        // 総合スコア表示
        let scoreIcon =
            match result.QualityLevel with
            | QualityLevel.Excellent -> "🟢"
            | QualityLevel.Good -> "🟡"
            | QualityLevel.Acceptable -> "🟠"
            | QualityLevel.Poor -> "🔴"
            | QualityLevel.Unacceptable -> "🔴"

        sb.AppendFormat("{0} 総合スコア: {1:F2} ({2})\n", scoreIcon, result.OverallScore, result.QualityLevel)
        |> ignore

        sb.AppendFormat("判定: {0}\n\n", if result.PassesThreshold then "✅ 合格" else "❌ 不合格")
        |> ignore

        // 各次元のスコア表示
        sb.AppendLine("📊 詳細評価:") |> ignore

        for metric in result.Metrics do
            let normalizedScore = metric.Score / metric.MaxScore

            let dimensionIcon =
                match metric.Dimension with
                | CodeQuality -> "💻"
                | FunctionalQuality -> "⚙️"
                | ProcessQuality -> "📋"
                | UserExperience -> "👤"
                | TechnicalCompleteness -> "🔧"

            sb.AppendFormat(
                "  {0} {1}: {2:F1}/100 ({3:F1}%)\n",
                dimensionIcon,
                metric.Dimension,
                metric.Score,
                normalizedScore * 100.0
            )
            |> ignore

        // 改善推奨事項
        if result.Recommendations.Length > 0 then
            sb.AppendLine("\n📝 改善推奨事項:") |> ignore

            for i, recommendation in result.Recommendations |> List.indexed do
                sb.AppendFormat("  {0}. {1}\n", i + 1, recommendation) |> ignore

        sb.AppendFormat("\n⏰ 評価時刻: {0:HH:mm:ss}\n", result.EvaluatedAt) |> ignore
        sb.AppendFormat("👤 評価者: {0}\n", result.EvaluatedBy) |> ignore

        sb.ToString()

    /// レビュー結果をUI表示用にフォーマット
    member this.FormatReviewResult(result: ReviewResult) : string =
        let sb = StringBuilder()

        sb.AppendFormat("🔍 レビュー結果 - {0}\n\n", result.TaskId) |> ignore

        // 総合判定
        let approvalIcon = if result.Approved then "✅" else "❌"

        sb.AppendFormat(
            "{0} 総合判定: {1} (スコア: {2:F2})\n\n",
            approvalIcon,
            (if result.Approved then "承認" else "要改善"),
            result.ConsensusScore
        )
        |> ignore

        // レビューコメント
        if result.Comments.Length > 0 then
            sb.AppendLine("💬 レビューコメント:") |> ignore

            for comment in result.Comments do
                let priorityIcon =
                    match comment.Priority with
                    | TaskPriority.Critical -> "🟥"
                    | TaskPriority.High -> "🔴"
                    | TaskPriority.Medium -> "🟡"
                    | TaskPriority.Low -> "🟢"
                    | _ -> "⚪"

                sb.AppendFormat(
                    "  {0} {1} ({2}): {3:F2}\n",
                    priorityIcon,
                    comment.ReviewerId,
                    comment.Dimension,
                    comment.Score
                )
                |> ignore

                sb.AppendFormat("    {0}\n", comment.Comment) |> ignore

                if comment.Suggestions.Length > 0 then
                    sb.AppendLine("    💡 提案:") |> ignore

                    for suggestion in comment.Suggestions do
                        sb.AppendFormat("      • {0}\n", suggestion) |> ignore

        // 必要改善事項
        if result.RequiredImprovements.Length > 0 then
            sb.AppendLine("\n🔧 必要改善事項:") |> ignore

            for i, improvement in result.RequiredImprovements |> List.indexed do
                sb.AppendFormat("  {0}. {1}\n", i + 1, improvement) |> ignore

        sb.AppendFormat("\n⏰ レビュー時刻: {0:HH:mm:ss}\n", result.ReviewedAt) |> ignore

        sb.ToString()

    /// 代替案提案をUI表示用にフォーマット
    member this.FormatAlternativeProposals(proposals: AlternativeProposal list) : string =
        let sb = StringBuilder()

        sb.AppendLine("🔄 代替案提案:\n") |> ignore

        for i, proposal in proposals |> List.indexed do
            let difficultyIcon =
                if proposal.DifficultyScore >= 0.7 then "🔴"
                elif proposal.DifficultyScore >= 0.5 then "🟡"
                else "🟢"

            let feasibilityIcon =
                if proposal.FeasibilityScore >= 0.8 then "✅"
                elif proposal.FeasibilityScore >= 0.6 then "🟡"
                else "❌"

            sb.AppendFormat("  {0}. {1} {2}\n", i + 1, proposal.Title, proposal.Description)
            |> ignore

            sb.AppendFormat(
                "     {0} 難易度: {1:F1} | {2} 実現性: {3:F1}\n",
                difficultyIcon,
                proposal.DifficultyScore,
                feasibilityIcon,
                proposal.FeasibilityScore
            )
            |> ignore

            sb.AppendFormat(
                "     ⏱️ 見積: {0:F1}h | 🔧 {1}\n",
                proposal.EstimatedEffort.TotalHours,
                proposal.TechnicalApproach
            )
            |> ignore

            sb.AppendLine() |> ignore

        sb.ToString()

    /// 品質ゲート実行とUI更新
    member this.ExecuteQualityGateWithUI(task: ParsedTask, targetView: TextView) : unit =
        try
            // 評価中状態に設定
            let displayInfo =
                { TaskId = task.TaskId
                  TaskTitle = task.Title
                  EvaluationResult = None
                  ReviewResult = None
                  AlternativeProposals = None
                  DisplayState = Evaluating
                  LastUpdated = DateTime.UtcNow }

            currentDisplayInfo <- Some displayInfo

            // 評価中メッセージ表示
            let evaluatingMessage =
                sprintf
                    "🔄 品質ゲート実行中...\n\nタスク: %s\n評価開始: %s\n\n品質評価・レビュー・代替案生成を実行中です。"
                    task.Title
                    (DateTime.UtcNow.ToString("HH:mm:ss"))

            targetView.Text <- NStack.ustring.Make(evaluatingMessage)
            targetView.SetNeedsDisplay()

            logInfo "QualityGateUI" (sprintf "品質ゲート実行開始: %s" task.TaskId)

            // 品質ゲート実行
            match qualityGateManager.ExecuteQualityGate(task) with
            | Result.Ok(reviewResult, alternatives) ->
                // 結果表示状態に設定
                let updatedDisplayInfo =
                    { displayInfo with
                        ReviewResult = Some reviewResult
                        AlternativeProposals = alternatives
                        DisplayState = DisplayingResults
                        LastUpdated = DateTime.UtcNow }

                currentDisplayInfo <- Some updatedDisplayInfo

                // 結果を表示
                let resultText =
                    this.FormatComprehensiveQualityResult(task, reviewResult, alternatives)

                targetView.Text <- NStack.ustring.Make(resultText)
                targetView.SetNeedsDisplay()

                logInfo "QualityGateUI" (sprintf "品質ゲート完了: %s (承認: %b)" task.TaskId reviewResult.Approved)

            | Result.Error errorMsg ->
                let errorDisplayInfo =
                    { displayInfo with
                        DisplayState = RequiresAction
                        LastUpdated = DateTime.UtcNow }

                currentDisplayInfo <- Some errorDisplayInfo

                let errorText =
                    sprintf
                        "❌ 品質ゲート実行エラー\n\nタスク: %s\nエラー: %s\n\n時刻: %s"
                        task.Title
                        errorMsg
                        (DateTime.UtcNow.ToString("HH:mm:ss"))

                targetView.Text <- NStack.ustring.Make(errorText)
                targetView.SetNeedsDisplay()

                logError "QualityGateUI" (sprintf "品質ゲート実行エラー: %s - %s" task.TaskId errorMsg)

        with ex ->
            logError "QualityGateUI" (sprintf "品質ゲートUI実行例外: %s" ex.Message)

    /// 包括的品質結果をフォーマット
    member this.FormatComprehensiveQualityResult
        (task: ParsedTask, reviewResult: ReviewResult, alternatives: AlternativeProposal list option)
        : string =
        let sb = StringBuilder()

        sb.AppendFormat("🎯 包括的品質ゲート結果\n\n") |> ignore
        sb.AppendFormat("タスク: {0}\n", task.Title) |> ignore
        sb.AppendFormat("タスクID: {0}\n", task.TaskId) |> ignore
        sb.AppendFormat("実行時刻: {0:HH:mm:ss}\n\n", DateTime.UtcNow) |> ignore

        // レビュー結果
        sb.AppendLine("=" + String.replicate 50 "=") |> ignore
        sb.AppendLine(this.FormatReviewResult(reviewResult)) |> ignore

        // 代替案がある場合
        match alternatives with
        | Some alts when alts.Length > 0 ->
            sb.AppendLine("=" + String.replicate 50 "=") |> ignore
            sb.AppendLine(this.FormatAlternativeProposals(alts)) |> ignore
        | _ -> ()

        // 次のアクション
        sb.AppendLine("=" + String.replicate 50 "=") |> ignore
        sb.AppendLine("🚀 次のアクション:\n") |> ignore

        if reviewResult.Approved then
            sb.AppendLine("✅ 実装承認 - 開発継続可能です") |> ignore
        else
            sb.AppendLine("❌ 改善必要 - 以下の対応が必要です:") |> ignore

            for i, improvement in reviewResult.RequiredImprovements |> List.indexed do
                sb.AppendFormat("  {0}. {1}\n", i + 1, improvement) |> ignore

        match alternatives with
        | Some alts when alts.Length > 0 -> sb.AppendLine("\n🔄 代替案検討 - 必要に応じて別アプローチを選択可能") |> ignore
        | _ -> ()

        sb.ToString()

    /// 品質ゲートステータス取得
    member this.GetCurrentQualityGateStatus() : QualityGateDisplayState option =
        currentDisplayInfo |> Option.map (fun info -> info.DisplayState)

    /// 品質ゲート情報取得
    member this.GetCurrentQualityGateInfo() : QualityGateUIInfo option = currentDisplayInfo

    /// 品質ゲート表示クリア
    member this.ClearQualityGateDisplay(targetView: TextView) : unit =
        currentDisplayInfo <- None
        targetView.Text <- NStack.ustring.Make("品質ゲート準備完了\n\nタスクを選択して品質評価を実行してください。")
        targetView.SetNeedsDisplay()
        logInfo "QualityGateUI" "品質ゲート表示クリア完了"

    /// 品質ゲート統計情報を取得
    member this.GenerateQualityGateStatistics() : string =
        match currentDisplayInfo with
        | Some info ->
            let sb = StringBuilder()
            sb.AppendFormat("📊 品質ゲート統計\n\n") |> ignore
            sb.AppendFormat("最終評価タスク: {0}\n", info.TaskTitle) |> ignore
            sb.AppendFormat("表示状態: {0}\n", info.DisplayState) |> ignore
            sb.AppendFormat("最終更新: {0:HH:mm:ss}\n\n", info.LastUpdated) |> ignore

            match info.ReviewResult with
            | Some review ->
                sb.AppendFormat(
                    "レビュー結果: {0} (スコア: {1:F2})\n",
                    (if review.Approved then "承認" else "要改善"),
                    review.ConsensusScore
                )
                |> ignore

                sb.AppendFormat("コメント数: {0}件\n", review.Comments.Length) |> ignore
                sb.AppendFormat("改善事項: {0}件\n", review.RequiredImprovements.Length) |> ignore
            | None -> sb.AppendLine("レビュー結果: 未実行") |> ignore

            match info.AlternativeProposals with
            | Some alts -> sb.AppendFormat("代替案: {0}件提案\n", alts.Length) |> ignore
            | None -> sb.AppendLine("代替案: なし") |> ignore

            sb.ToString()
        | None -> "品質ゲート統計情報がありません。"
