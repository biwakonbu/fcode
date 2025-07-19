module FCode.ExternalIntegration.GitWorkflowAgent

open System
open System.Diagnostics
open FCode

// ===============================================
// Git統合エージェント（簡素化版）
// ===============================================

/// Git操作結果
type GitOperationResult =
    { Success: bool
      Output: string
      ErrorMessage: string option }

/// Gitワークフロー自動化エージェント（基本実装）
type GitWorkflowAgent(workingDirectory: string) =

    let logger = Logger.Logger()

    /// Git操作の基本実行
    let executeGitCommand (args: string list) =
        async {
            try
                let processInfo = ProcessStartInfo()
                processInfo.FileName <- "git"
                processInfo.Arguments <- String.Join(" ", args)
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

                logger.Info("GitWorkflowAgent", $"Git command executed: git {String.Join(' ', args)}")

                if proc.ExitCode = 0 then
                    return
                        { Success = true
                          Output = output.Trim()
                          ErrorMessage = None }
                else
                    logger.Error("GitWorkflowAgent", $"Git command failed: {errorOutput}")

                    return
                        { Success = false
                          Output = ""
                          ErrorMessage = Some errorOutput }

            with ex ->
                logger.Error("GitWorkflowAgent", $"Git execution error: {ex.Message}")

                return
                    { Success = false
                      Output = ""
                      ErrorMessage = Some ex.Message }
        }

    /// ブランチ作成
    member _.CreateBranch(branchName: string, ?sourceBranch: string) =
        async {
            try
                let sourceBranch = defaultArg sourceBranch "main"

                // 入力検証
                if String.IsNullOrWhiteSpace(branchName) then
                    return
                        { Success = false
                          Output = ""
                          ErrorMessage = Some "Branch name cannot be empty" }
                else
                    // 元ブランチに切り替え
                    let! switchResult = executeGitCommand [ "checkout"; sourceBranch ]

                    if not switchResult.Success then
                        return
                            { Success = false
                              Output = ""
                              ErrorMessage = Some $"Failed to switch to source branch: {switchResult.ErrorMessage}" }
                    else
                        // 新しいブランチを作成
                        let! createResult = executeGitCommand [ "checkout"; "-b"; branchName ]

                        if createResult.Success then
                            logger.Info("GitWorkflowAgent", $"Branch created successfully: {branchName}")
                            return createResult
                        else
                            return createResult

            with ex ->
                logger.Error("GitWorkflowAgent", $"Branch creation error: {ex.Message}")

                return
                    { Success = false
                      Output = ""
                      ErrorMessage = Some ex.Message }
        }

    /// コミット作成
    member _.CreateCommit(files: string list, ?customMessage: string) =
        async {
            try
                // ファイルをステージング
                for file in files do
                    let! addResult = executeGitCommand [ "add"; file ]

                    if not addResult.Success then
                        logger.Warning("GitWorkflowAgent", $"Failed to add file {file}: {addResult.ErrorMessage}")

                // コミットメッセージ生成
                let message =
                    match customMessage with
                    | Some msg -> msg
                    | None ->
                        let fileNames = files |> List.map System.IO.Path.GetFileName |> String.concat ", "
                        $"Update {fileNames}: v2.0-1 external tools integration implementation"

                // コミット実行
                let! commitResult = executeGitCommand [ "commit"; "-m"; message ]

                if commitResult.Success then
                    logger.Info("GitWorkflowAgent", $"Commit created successfully: {message}")
                    return commitResult
                else
                    return commitResult

            with ex ->
                logger.Error("GitWorkflowAgent", $"Commit creation error: {ex.Message}")

                return
                    { Success = false
                      Output = ""
                      ErrorMessage = Some ex.Message }
        }

    /// プルリクエスト作成（GitHub CLI使用）
    member _.CreatePullRequest(title: string, description: string, targetBranch: string) =
        async {
            try
                if String.IsNullOrWhiteSpace(title) then
                    return
                        { Success = false
                          Output = ""
                          ErrorMessage = Some "PR title cannot be empty" }
                else
                    // GitHub CLI でプルリクエスト作成
                    let processInfo = ProcessStartInfo()
                    processInfo.FileName <- "gh"

                    processInfo.Arguments <-
                        $"pr create --title \"{title}\" --body \"{description}\" --base {targetBranch}"

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
                        logger.Info("GitWorkflowAgent", $"Pull request created successfully: {title}")

                        return
                            { Success = true
                              Output = output
                              ErrorMessage = None }
                    else
                        logger.Error("GitWorkflowAgent", $"PR creation failed: {errorOutput}")

                        return
                            { Success = false
                              Output = ""
                              ErrorMessage = Some errorOutput }

            with ex ->
                logger.Error("GitWorkflowAgent", $"PR creation error: {ex.Message}")

                return
                    { Success = false
                      Output = ""
                      ErrorMessage = Some ex.Message }
        }
