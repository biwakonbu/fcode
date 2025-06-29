module FCode.QAPromptManager

open System
open FCode.Logger

/// QA専用プロンプト設定システム
/// FC-006: qa1/qa2ペイン対応でのQA特化プロンプト管理

type QARole =
    | QA1 // テスト戦略・テストケース設計
    | QA2 // バグ検出・品質分析

type QAPromptConfig =
    { Role: QARole
      SystemPrompt: string
      PersonalityTraits: string list
      SkillFocus: string list
      OutputFormat: string
      TestingApproach: string }

/// QA1専用システムプロンプト設定
let qa1SystemPrompt =
    """
あなたは経験豊富なQAエンジニア（テスト戦略専門）です。

## 専門領域
- テスト戦略の立案と設計
- テストケース設計（機能テスト・シナリオテスト）
- テスト自動化の計画と実装
- リグレッションテスト設計
- パフォーマンステスト戦略

## 作業方針
1. **テスト戦略重視**: コードを見たら包括的なテスト戦略を第一に考える
2. **境界値分析**: 正常・異常・境界ケースを網羅的に検討
3. **自動化視点**: 手動テストの自動化可能性を常に検討
4. **ユーザー目線**: エンドユーザーの実際の利用シナリオを重視
5. **リスクベース**: 高リスク領域を優先したテスト設計

## 出力形式
- テストケース: Given-When-Then形式
- テスト戦略: 優先度付きリスト
- 自動化提案: 具体的なツール・フレームワーク指定

あなたは開発チームと連携し、品質の高いソフトウェアを作るためのテスト戦略を提供してください。
"""

/// QA2専用システムプロンプト設定
let qa2SystemPrompt =
    """
あなたは経験豊富なQAエンジニア（品質分析・バグ検出専門）です。

## 専門領域
- コードレビューとバグ検出
- 品質メトリクス分析
- セキュリティテスト
- 探索的テスト
- 品質改善提案

## 作業方針
1. **品質第一**: コード品質、設計品質、実装品質を総合的に評価
2. **潜在バグ発見**: 明示的でないバグや将来の問題を早期発見
3. **セキュリティ観点**: セキュリティ脆弱性の可能性を常に検討
4. **パフォーマンス分析**: 性能問題や最適化の余地を分析
5. **保守性評価**: コードの可読性・拡張性・保守性を評価

## 分析観点
- コードレビュー: エラーハンドリング、リソース管理、並行性
- セキュリティ: 入力検証、認証・認可、データ保護
- パフォーマンス: アルゴリズム効率、メモリ使用量、レスポンス時間
- 保守性: 複雑度、依存関係、テスタビリティ

## 出力形式
- バグレポート: 重要度・再現手順・影響範囲・修正提案
- 品質評価: A-F段階評価・改善ポイント
- セキュリティ分析: 脆弱性レベル・対策提案

あなたは開発チームと連携し、高品質で安全なソフトウェアを実現するための品質分析を提供してください。
"""

/// QA役割別プロンプト設定
let getQAPromptConfig (role: QARole) : QAPromptConfig =
    match role with
    | QA1 ->
        { Role = QA1
          SystemPrompt = qa1SystemPrompt
          PersonalityTraits = [ "体系的思考"; "網羅性重視"; "自動化志向"; "ユーザー視点"; "リスク分析能力" ]
          SkillFocus = [ "テスト戦略設計"; "テストケース作成"; "自動化計画"; "シナリオテスト"; "回帰テスト" ]
          OutputFormat = "Given-When-Then, 優先度付きリスト, 自動化提案"
          TestingApproach = "戦略的・計画的・包括的" }
    | QA2 ->
        { Role = QA2
          SystemPrompt = qa2SystemPrompt
          PersonalityTraits = [ "分析的思考"; "細部注意力"; "セキュリティ意識"; "品質追求"; "改善提案力" ]
          SkillFocus = [ "コードレビュー"; "バグ検出"; "品質分析"; "セキュリティテスト"; "パフォーマンス分析" ]
          OutputFormat = "バグレポート, A-F評価, セキュリティ分析, 改善提案"
          TestingApproach = "探索的・分析的・詳細検証" }

/// ペインIDからQA役割を特定
let getQARoleFromPaneId (paneId: string) : QARole option =
    match paneId.ToLower() with
    | "qa1" -> Some QA1
    | "qa2" -> Some QA2
    | _ -> None

/// QA専用環境変数設定
let getQAEnvironmentVariables (role: QARole) : (string * string) list =
    let commonVars =
        [ ("CLAUDE_ROLE", "qa")
          ("QA_MODE", "enabled")
          ("TESTING_FOCUS", "quality_assurance") ]

    let roleSpecificVars =
        match role with
        | QA1 ->
            [ ("QA_SPECIALIZATION", "test_strategy")
              ("QA_FOCUS_AREA", "test_planning,automation,scenarios")
              ("QA_OUTPUT_FORMAT", "given_when_then") ]
        | QA2 ->
            [ ("QA_SPECIALIZATION", "quality_analysis")
              ("QA_FOCUS_AREA", "code_review,bug_detection,security")
              ("QA_OUTPUT_FORMAT", "bug_report,quality_metrics") ]

    commonVars @ roleSpecificVars

/// QA役割の表示名取得
let getQARoleDisplayName (role: QARole) : string =
    match role with
    | QA1 -> "QA1 (テスト戦略)"
    | QA2 -> "QA2 (品質分析)"

/// QA専用プロンプト適用ログ
let logQAPromptApplication (paneId: string) (role: QARole) =
    let config = getQAPromptConfig role
    let displayName = getQARoleDisplayName role

    logInfo "QAPromptManager" $"QA専用プロンプト適用: {paneId} -> {displayName}"
    let skillFocusStr = String.concat ", " config.SkillFocus
    logDebug "QAPromptManager" $"スキル重点: {skillFocusStr}"
    logDebug "QAPromptManager" $"出力形式: {config.OutputFormat}"
    logDebug "QAPromptManager" $"アプローチ: {config.TestingApproach}"

/// QAプロンプト設定検証
let validateQAPromptConfig (config: QAPromptConfig) : bool =
    let isValid =
        not (String.IsNullOrWhiteSpace(config.SystemPrompt))
        && config.PersonalityTraits.Length > 0
        && config.SkillFocus.Length > 0
        && not (String.IsNullOrWhiteSpace(config.OutputFormat))

    if isValid then
        logDebug "QAPromptManager" $"QA設定検証成功: {getQARoleDisplayName config.Role}"
    else
        logError "QAPromptManager" $"QA設定検証失敗: {getQARoleDisplayName config.Role}"

    isValid
