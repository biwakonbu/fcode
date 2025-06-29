module FCode.UXPromptManager

open System
open FCode.Logger

/// UX専用プロンプト設定システム
/// FC-007: uxペイン対応でのUX特化プロンプト管理

type UXRole = UX // ユーザビリティ・UI/UXデザイン専門

type UXPromptConfig =
    { Role: UXRole
      SystemPrompt: string
      PersonalityTraits: string list
      SkillFocus: string list
      OutputFormat: string
      DesignApproach: string }

/// UX専用システムプロンプト設定
let uxSystemPrompt =
    """
あなたは経験豊富なUX/UIデザインの専門家です。

## 専門領域
- ユーザビリティ設計と評価
- UI/UXデザイン設計
- ワイヤーフレーム・プロトタイプ作成
- アクセシビリティ設計
- ユーザーリサーチ・ユーザーテスト

## 作業方針
1. **ユーザー中心設計**: 常にエンドユーザーの体験を最優先に考える
2. **アクセシビリティ重視**: 多様なユーザーが利用できるインクルーシブデザイン
3. **使いやすさ追求**: 直感的で学習コストの低いインターフェース設計
4. **データ駆動**: ユーザー行動データやフィードバックに基づく改善提案
5. **反復改善**: プロトタイプ→テスト→改善のサイクル重視

## 設計観点
- ユーザビリティ: 効率性、有効性、満足度、学習容易性
- アクセシビリティ: WCAG準拠、スクリーンリーダー対応、キーボード操作
- 視覚デザイン: 情報階層、色彩設計、タイポグラフィ、一貫性
- インタラクション: ユーザーフロー、ナビゲーション、フィードバック

## 出力形式
- ワイヤーフレーム: 構造化された画面設計案
- ユーザーフロー: ステップバイステップの操作手順
- 改善提案: 優先度付き・根拠明示の具体的提案
- 評価レポート: ユーザビリティ評価・改善点

あなたは開発チームと連携し、優れたユーザー体験を実現するためのUX/UI設計を提供してください。
"""

/// UX役割別プロンプト設定
let getUXPromptConfig (role: UXRole) : UXPromptConfig =
    match role with
    | UX ->
        { Role = UX
          SystemPrompt = uxSystemPrompt
          PersonalityTraits = [ "ユーザー共感力"; "デザイン思考"; "問題解決志向"; "協調性"; "継続改善意識" ]
          SkillFocus = [ "ユーザビリティ設計"; "ワイヤーフレーム作成"; "アクセシビリティ"; "ユーザーテスト"; "UI改善提案" ]
          OutputFormat = "ワイヤーフレーム, ユーザーフロー, 改善提案, 評価レポート"
          DesignApproach = "ユーザー中心・反復改善・データ駆動" }

/// ペインIDからUX役割を特定
let getUXRoleFromPaneId (paneId: string) : UXRole option =
    match paneId.ToLower() with
    | "ux" -> Some UX
    | _ -> None

/// UX専用環境変数設定
let getUXEnvironmentVariables (role: UXRole) : (string * string) list =
    let commonVars =
        [ ("CLAUDE_ROLE", "ux")
          ("UX_MODE", "enabled")
          ("DESIGN_FOCUS", "user_experience") ]

    let roleSpecificVars =
        match role with
        | UX ->
            [ ("UX_SPECIALIZATION", "ui_ux_design")
              ("UX_FOCUS_AREA", "usability,accessibility,user_flow")
              ("UX_OUTPUT_FORMAT", "wireframe,user_flow,improvement_proposal") ]

    commonVars @ roleSpecificVars

/// UX役割の表示名取得
let getUXRoleDisplayName (role: UXRole) : string =
    match role with
    | UX -> "UX (UI/UXデザイン)"

/// UX専用プロンプト適用ログ
let logUXPromptApplication (paneId: string) (role: UXRole) =
    let config = getUXPromptConfig role
    let displayName = getUXRoleDisplayName role

    logInfo "UXPromptManager" $"UX専用プロンプト適用: {paneId} -> {displayName}"
    let skillFocusStr = String.concat ", " config.SkillFocus
    logDebug "UXPromptManager" $"スキル重点: {skillFocusStr}"
    logDebug "UXPromptManager" $"出力形式: {config.OutputFormat}"
    logDebug "UXPromptManager" $"アプローチ: {config.DesignApproach}"

/// UXプロンプト設定検証
let validateUXPromptConfig (config: UXPromptConfig) : bool =
    let isValid =
        not (String.IsNullOrWhiteSpace(config.SystemPrompt))
        && config.PersonalityTraits.Length > 0
        && config.SkillFocus.Length > 0
        && not (String.IsNullOrWhiteSpace(config.OutputFormat))

    if isValid then
        logDebug "UXPromptManager" $"UX設定検証成功: {getUXRoleDisplayName config.Role}"
    else
        logError "UXPromptManager" $"UX設定検証失敗: {getUXRoleDisplayName config.Role}"

    isValid
