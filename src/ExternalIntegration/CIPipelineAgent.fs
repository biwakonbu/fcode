module FCode.ExternalIntegration.CIPipelineAgent

open System
open System.Diagnostics
open FCode

// ===============================================
// CI/CDパイプライン統合エージェント（簡素化版）
// ===============================================

/// CI/CDプラットフォーム種別
type CIPlatform =
    | GitHubActions
    | GitLabCI
    | Jenkins

/// パイプライン実行結果
type PipelineResult =
    { Success: bool
      Output: string
      ErrorMessage: string option }

/// CI/CDパイプライン統合エージェント（基本実装）
type CIPipelineAgent(platform: CIPlatform, workingDirectory: string) =

    let logger = Logger.Logger()

    /// テスト実行
    member _.RunTestSuite(testSuites: string list) =
        async {
            try
                let results = System.Collections.Generic.List<PipelineResult>()

                for suite in testSuites do
                    logger.Info("CIPipelineAgent", $"Running test suite: {suite}")

                    // テストコマンド実行
                    let processInfo = ProcessStartInfo()
                    processInfo.FileName <- "dotnet"
                    processInfo.Arguments <- $"test {suite} --logger trx"
                    processInfo.WorkingDirectory <- workingDirectory
                    processInfo.RedirectStandardOutput <- true
                    processInfo.RedirectStandardError <- true
                    processInfo.UseShellExecute <- false
                    processInfo.CreateNoWindow <- true

                    use proc = new Process(StartInfo = processInfo)
                    proc.Start() |> ignore

                    let output = proc.StandardOutput.ReadToEnd()
                    let errorOutput = proc.StandardError.ReadToEnd()

                    proc.WaitForExit()

                    if proc.ExitCode = 0 then
                        let testResult =
                            { Success = true
                              Output = output
                              ErrorMessage = None }

                        results.Add(testResult)
                        logger.Info("CIPipelineAgent", $"Test suite passed: {suite}")
                    else
                        let testResult =
                            { Success = false
                              Output = ""
                              ErrorMessage = Some errorOutput }

                        results.Add(testResult)
                        logger.Error("CIPipelineAgent", $"Test suite failed: {suite}")

                // 全体的な結果集約
                let overallSuccess = results |> Seq.forall (fun r -> r.Success)
                let combinedOutput = results |> Seq.map (fun r -> r.Output) |> String.concat "\n"

                return
                    { Success = overallSuccess
                      Output = combinedOutput
                      ErrorMessage = None }

            with ex ->
                logger.Error("CIPipelineAgent", $"Test execution error: {ex.Message}")

                return
                    { Success = false
                      Output = ""
                      ErrorMessage = Some ex.Message }
        }

    /// デプロイメント実行
    member _.DeployToEnvironment(environment: string, version: string) =
        async {
            try
                logger.Info("CIPipelineAgent", $"Starting deployment to {environment} with version {version}")

                match platform with
                | GitHubActions ->
                    // GitHub Actions デプロイメントワークフロートリガー
                    let processInfo = ProcessStartInfo()
                    processInfo.FileName <- "gh"

                    processInfo.Arguments <-
                        $"workflow run deploy --ref main -f environment={environment} -f version={version}"

                    processInfo.WorkingDirectory <- workingDirectory
                    processInfo.RedirectStandardOutput <- true
                    processInfo.RedirectStandardError <- true
                    processInfo.UseShellExecute <- false
                    processInfo.CreateNoWindow <- true

                    use proc = new Process(StartInfo = processInfo)
                    proc.Start() |> ignore

                    let output = proc.StandardOutput.ReadToEnd()
                    let errorOutput = proc.StandardError.ReadToEnd()

                    proc.WaitForExit()

                    if proc.ExitCode = 0 then
                        return
                            { Success = true
                              Output = output
                              ErrorMessage = None }
                    else
                        return
                            { Success = false
                              Output = ""
                              ErrorMessage = Some errorOutput }

                | _ ->
                    logger.Warning("CIPipelineAgent", $"Deployment not implemented for platform: {platform}")

                    return
                        { Success = false
                          Output = ""
                          ErrorMessage = Some "Deployment not supported for this platform" }

            with ex ->
                logger.Error("CIPipelineAgent", $"Deployment error: {ex.Message}")

                return
                    { Success = false
                      Output = ""
                      ErrorMessage = Some ex.Message }
        }
