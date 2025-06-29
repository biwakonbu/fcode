module FCode.PMPromptManager

open FCode.Logger

// PM役割定義
type PMRole =
    | ProjectManager
    | ProductManager

// PM設定
type PMPromptConfig =
    { SystemPrompt: string
      EnvironmentVars: (string * string) list
      DisplayName: string }

// PMロール表示名取得
let getPMRoleDisplayName (role: PMRole) =
    match role with
    | ProjectManager -> "プロジェクトマネージャー"
    | ProductManager -> "プロダクトマネージャー"

// paneIdからPMロール判定
let getPMRoleFromPaneId (paneId: string) =
    match paneId with
    | "pm" -> Some ProjectManager
    | id when id.Contains("PM") || id.Contains("timeline") -> Some ProjectManager
    | _ -> None

// PM専用プロンプト設定取得
let getPMPromptConfig (role: PMRole) =
    match role with
    | ProjectManager ->
        { SystemPrompt =
            """あなたはプロジェクトマネージャーです。以下の観点で支援してください：

【進捗管理】
- タスクの進捗状況把握と課題特定
- スケジュール調整と優先度設定
- チームメンバーの作業負荷分散

【リスク管理】
- プロジェクトリスクの早期発見と対策
- 技術的負債・品質問題の予防
- ステークホルダー間の調整

【品質管理】
- 成果物の品質基準設定と評価
- チーム間のコミュニケーション促進
- 継続的改善プロセスの実行

【統合管理】
- dev1-3, qa1-2, uxペインの活動統合監視
- プロジェクト全体のサマリー・レポート作成
- 意思決定支援と戦略的提案

必ず日本語で回答し、具体的で実行可能な提案を行ってください。"""
          EnvironmentVars =
            [ ("CLAUDE_ROLE", "pm")
              ("PM_FOCUS", "project_management")
              ("PM_PERSPECTIVE", "integration")
              ("PM_TEAM_SIZE", "7_panes")
              ("PM_METHODOLOGY", "agile_kanban") ]
          DisplayName = "プロジェクトマネージャー" }
    | ProductManager ->
        { SystemPrompt =
            """あなたはプロダクトマネージャーです。以下の観点で支援してください：

【プロダクト戦略】
- ユーザー価値の最大化と機能優先度決定
- プロダクトロードマップの策定・調整
- 市場ニーズとテクニカル実現性のバランス

【ステークホルダー管理】
- ユーザー・ビジネス要件の収集と整理
- 開発チームとビジネス側の橋渡し
- 意思決定の迅速化と透明性確保

【プロダクト分析】
- 開発成果物のビジネス価値評価
- UXとテクニカル品質の統合評価
- プロダクト改善のためのデータ分析

【チーム協調】
- dev, qa, uxチームの協調最適化
- プロダクトビジョンの共有と浸透
- 継続的価値提供のためのプロセス改善

必ず日本語で回答し、ビジネス価値と技術実現性を両立する提案を行ってください。"""
          EnvironmentVars =
            [ ("CLAUDE_ROLE", "pm")
              ("PM_FOCUS", "product_management")
              ("PM_PERSPECTIVE", "business_value")
              ("PM_TEAM_SIZE", "7_panes")
              ("PM_METHODOLOGY", "lean_startup") ]
          DisplayName = "プロダクトマネージャー" }

// PM専用環境変数取得
let getPMEnvironmentVariables (role: PMRole) =
    let config = getPMPromptConfig role
    config.EnvironmentVars

// PMプロンプト適用ログ出力
let logPMPromptApplication (paneId: string) (role: PMRole) =
    let displayName = getPMRoleDisplayName role
    logInfo "PMPrompt" $"PM専用プロンプト適用: ペイン={paneId}, 役割={displayName}"

    let config = getPMPromptConfig role
    logDebug "PMPrompt" $"システムプロンプト文字数: {config.SystemPrompt.Length}"
    logDebug "PMPrompt" $"環境変数設定数: {config.EnvironmentVars.Length}"

    config.EnvironmentVars
    |> List.iter (fun (key, value) -> logDebug "PMPrompt" $"PM環境変数: {key}={value}")
