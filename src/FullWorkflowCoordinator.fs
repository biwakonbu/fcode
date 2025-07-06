module FCode.FullWorkflowCoordinator

// 最小限の動作する実装に移行
// リファクタリング完了: 単一責務違反・設計問題解決済み

open FCode.WorkflowCore.WorkflowTypes
open FCode.WorkflowCore.MinimalWorkflowCoordinator

// 後方互換性のため旧型名をエイリアス
type WorkflowConfig = FCode.WorkflowCore.WorkflowTypes.WorkflowConfig
type WorkflowStage = FCode.WorkflowCore.WorkflowTypes.WorkflowStage
type WorkflowState = FCode.WorkflowCore.WorkflowTypes.WorkflowState

// 旧インターフェース用の後方互換ラッパー
type FullWorkflowCoordinator() =
    inherit FCode.WorkflowCore.MinimalWorkflowCoordinator.FullWorkflowCoordinator()
