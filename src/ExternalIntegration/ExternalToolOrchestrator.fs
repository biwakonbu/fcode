module FCode.ExternalIntegration.ExternalToolOrchestrator

open System
open FCode
open FCode.ExternalIntegration.GitWorkflowAgent
open FCode.ExternalIntegration.CIPipelineAgent
open FCode.ExternalIntegration.CloudProviderIntegration

// ===============================================
// 外部ツール統合オーケストレーター（簡素化版）
// ===============================================

/// 統合ワークフロー結果
type OrchestrationResult =
    { Success: bool
      StepsCompleted: int
      TotalSteps: int
      Output: string list
      ErrorMessage: string option }

/// 外部ツール統合オーケストレーター
type ExternalToolOrchestrator(workingDirectory: string) =

    let logger = Logger.Logger()
    let gitAgent = GitWorkflowAgent(workingDirectory)
    let ciAgent = CIPipelineAgent(GitHubActions, workingDirectory)
    let cloudAgent = CloudProviderIntegrationFacade(Docker, "us-east-1")

    /// 機能開発ワークフロー実行
    member _.ExecuteFeatureDevelopmentWorkflow(featureName: string, files: string list) =
        async {
            try
                logger.Info("Orchestrator", $"Starting feature development workflow: {featureName}")
                let mutable outputs = []
                let mutable completedSteps = 0
                let totalSteps = 4

                // Step 1: ブランチ作成
                let! branchResult = gitAgent.CreateBranch($"feature/{featureName}", sourceBranch = "main")

                if branchResult.Success then
                    outputs <- $"Branch created: feature/{featureName}" :: outputs
                    completedSteps <- completedSteps + 1
                    logger.Info("Orchestrator", "Feature branch created successfully")
                else
                    logger.Error("Orchestrator", $"Branch creation failed")

                    return
                        { Success = false
                          StepsCompleted = completedSteps
                          TotalSteps = totalSteps Output = List.rev outputs
                          ErrorMessage = branchResult.ErrorMessage }

                // Step 2: ファイル変更とコミット
                let! commitResult = gitAgent.CreateCommit(files, customMessage = $"Implement {featureName} feature")

                if commitResult.Success then
                    outputs <- "Commit created successfully" :: outputs
                    completedSteps <- completedSteps + 1
                    logger.Info("Orchestrator", "Changes committed successfully")
                else
                    logger.Error("Orchestrator", "Commit creation failed")

                    return
                        { Success = false
                          StepsCompleted = completedSteps
                          TotalSteps = totalSteps Output = List.rev outputs
                          ErrorMessage = commitResult.ErrorMessage }

                // Step 3: テスト実行
                let! testResult = ciAgent.RunTestSuite([ "tests/fcode.Tests.fsproj" ])

                if testResult.Success then
                    outputs <- "Tests passed" :: outputs
                    completedSteps <- completedSteps + 1
                    logger.Info("Orchestrator", "Tests executed successfully")
                else
                    logger.Warning("Orchestrator", "Tests failed")
                    outputs <- "Tests failed" :: outputs

                // Step 4: プルリクエスト作成
                let! prResult =
                    gitAgent.CreatePullRequest(
                        $"feat: {featureName}",
                        $"Implements {featureName} functionality",
                        "main"
                    )

                if prResult.Success then
                    outputs <- "PR created successfully" :: outputs
                    completedSteps <- completedSteps + 1
                    logger.Info("Orchestrator", "Pull request created successfully")
                else
                    logger.Warning("Orchestrator", "PR creation failed")
                    outputs <- "PR creation failed" :: outputs

                return
                    { Success = (completedSteps = totalSteps)
                      StepsCompleted = completedSteps
                      TotalSteps = totalSteps Output = List.rev outputs
                      ErrorMessage = None }

            with ex ->
                logger.Error("Orchestrator", $"Feature development workflow error: {ex.Message}")

                return
                    { Success = false
                      StepsCompleted = 0
                      TotalSteps = 4 Output = []
                      ErrorMessage = Some ex.Message }
        }

    /// サービス健全性監視
    member _.MonitorServiceHealth(serviceName: string) =
        async {
            try
                logger.Info("Orchestrator", $"Monitoring service health: {serviceName}")

                let! healthResult = cloudAgent.MonitorServiceHealth(serviceName)

                return
                    { Success = healthResult.Success
                      StepsCompleted = 1
                      TotalSteps = 1 Output = [ healthResult.Output ]
                      ErrorMessage = healthResult.ErrorMessage }

            with ex ->
                logger.Error("Orchestrator", $"Service monitoring error: {ex.Message}")

                return
                    { Success = false
                      StepsCompleted = 0
                      TotalSteps = 1 Output = []
                      ErrorMessage = Some ex.Message }
        }
