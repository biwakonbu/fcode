module FCode.DevOpsIntegration

open System
open System.Collections.Generic
open System.Diagnostics
open System.IO
open System.Text.Json
open FCode.AgentCLI
open FCode.Logger

// ===============================================
// 開発フロー統合定義
// ===============================================

/// 開発ツール種別
type DevToolType =
    | Git // Git バージョン管理
    | Docker // Docker コンテナ
    | Kubernetes // Kubernetes オーケストレーション
    | Jenkins // Jenkins CI/CD
    | GitHubActions // GitHub Actions
    | GitLabCI // GitLab CI/CD
    | AWS // AWS クラウドサービス
    | GCP // Google Cloud Platform
    | Azure // Microsoft Azure
    | Terraform // インフラストラクチャコード
    | Ansible // 構成管理
    | Prometheus // 監視・メトリクス
    | Grafana // ダッシュボード・可視化
    | ELK // Elasticsearch, Logstash, Kibana

/// 開発フロー段階
type DevFlowStage =
    | Planning // 計画・設計
    | Development // 開発・実装
    | Testing // テスト・品質保証
    | Building // ビルド・パッケージング
    | Deployment // デプロイ・リリース
    | Monitoring // 監視・運用
    | Maintenance // メンテナンス・改善

/// 統合コマンド定義
type DevToolCommand =
    { Tool: DevToolType
      Command: string
      Arguments: string list
      WorkingDirectory: string option
      EnvironmentVariables: Map<string, string>
      ExpectedDuration: TimeSpan
      RequiredPermissions: string list }

/// 実行結果
type DevToolResult =
    { Command: DevToolCommand
      ExitCode: int
      StandardOutput: string
      StandardError: string
      Duration: TimeSpan
      Success: bool
      Timestamp: DateTime }

// ===============================================
// プロセス共通ヘルパー
// ===============================================

/// ProcessStartInfo作成ヘルパー
module private ProcessHelper =
    let createProcessStartInfo (command: string) (arguments: string) (workingDirectory: string option) =
        let psi = ProcessStartInfo(command, arguments)
        workingDirectory |> Option.iter (fun dir -> psi.WorkingDirectory <- dir)
        psi.UseShellExecute <- false
        psi.RedirectStandardOutput <- true
        psi.RedirectStandardError <- true
        psi

// ===============================================
// Git統合マネージャー
// ===============================================

/// Git操作専門マネージャー
type GitIntegrationManager() =

    /// Git状態確認
    member _.GetRepositoryStatus(repoPath: string) =
        let gitCommand =
            { Tool = Git
              Command = "git"
              Arguments = [ "status"; "--porcelain" ]
              WorkingDirectory = Some repoPath
              EnvironmentVariables = Map.empty
              ExpectedDuration = TimeSpan.FromSeconds(5.0)
              RequiredPermissions = [ "read" ] }

        try
            let stopwatch = Diagnostics.Stopwatch.StartNew()

            let psi =
                ProcessHelper.createProcessStartInfo "git" "status --porcelain" (Some repoPath)

            use proc = Process.Start(psi)

            if not (proc.WaitForExit(5000)) then
                // セキュリティ: Process Resource管理強化
                proc.Kill()
                proc.WaitForExit(1000) |> ignore // 確実な終了待機
                logError "GitIntegrationManager" "Git status check timed out"

            stopwatch.Stop()
            let output = proc.StandardOutput.ReadToEnd()
            let errors = proc.StandardError.ReadToEnd()

            { Command = gitCommand
              ExitCode = proc.ExitCode
              StandardOutput = output
              StandardError = errors
              Duration = stopwatch.Elapsed
              Success = proc.ExitCode = 0
              Timestamp = DateTime.Now }
        with ex ->
            // セキュリティ: Information Disclosure対策
            logError "GitIntegrationManager" "Git status check failed"
            logDebug "GitIntegrationManager" $"Git状態確認エラー詳細: {ex.Message}"

            { Command = gitCommand
              ExitCode = -1
              StandardOutput = ""
              StandardError = "Git operation failed"
              Duration = TimeSpan.Zero
              Success = false
              Timestamp = DateTime.Now }

    /// ブランチ作成・切り替え
    member _.CreateAndSwitchBranch (repoPath: string) (branchName: string) =
        // セキュリティ: Command Injection対策
        let validateBranchName (name: string) =
            if String.IsNullOrWhiteSpace(name) then
                raise (ArgumentException("Branch name cannot be empty"))

            let dangerousChars =
                [ "../"; "..\\"; ";"; "|"; "&"; "`"; "$"; "'"; "\""; "\n"; "\r"; "\t" ]

            if dangerousChars |> List.exists name.Contains then
                raise (ArgumentException("Invalid characters in branch name"))

            if name.Length > 100 then
                raise (ArgumentException("Branch name too long"))

            name.Trim()

        try
            let safeBranchName = validateBranchName branchName

            let psi =
                ProcessHelper.createProcessStartInfo "git" $"checkout -b {safeBranchName}" (Some repoPath)

            use proc = Process.Start(psi)

            if not (proc.WaitForExit(10000)) then
                // セキュリティ: Process Resource管理強化
                proc.Kill()
                proc.WaitForExit(1000) |> ignore // 確実な終了待機
                logError "GitIntegrationManager" "Git branch creation timed out"

            let success = proc.ExitCode = 0
            logInfo "GitIntegrationManager" $"ブランチ作成・切り替え: {branchName} (成功: {success})"
            success
        with ex ->
            // セキュリティ: Information Disclosure対策
            logError "GitIntegrationManager" $"ブランチ作成エラー: {branchName}"
            logDebug "GitIntegrationManager" $"ブランチ作成エラー詳細: {ex.Message}"
            false

// ===============================================
// Docker統合マネージャー
// ===============================================

/// Docker操作専門マネージャー
type DockerIntegrationManager() =

    // Docker PS フォーマット定数
    let dockerPsFormat =
        "ps --format \"table {{.ID}}\\t{{.Image}}\\t{{.Status}}\\t{{.Names}}\""

    /// Dockerコンテナ状態確認
    member _.GetContainerStatus() =
        try
            let psi = ProcessHelper.createProcessStartInfo "docker" dockerPsFormat None

            use proc = Process.Start(psi)

            if not (proc.WaitForExit(10000)) then
                proc.Kill()
                logError "DockerIntegrationManager" "Docker container status check timed out"

            let output = proc.StandardOutput.ReadToEnd()
            logInfo "DockerIntegrationManager" "Dockerコンテナ状態確認完了"
            Some output
        with ex ->
            logError "DockerIntegrationManager" $"Dockerコンテナ状態確認エラー: {ex.Message}"
            None

// ===============================================
// CI/CD統合マネージャー
// ===============================================

/// CI/CDパイプライン統合
type CICDIntegrationManager() =

    /// GitHub Actions ワークフロー生成
    member _.GenerateGitHubActionsWorkflow (projectType: string) (testCommand: string) (buildCommand: string) =
        // TODO: 構造化アプローチでYAML生成改善 - 文字列連結からYAMLライブラリへ移行検討
        let workflowParts =
            [ "name: CI/CD Pipeline"
              ""
              "on:"
              "  push:"
              "    branches: [ main, develop ]"
              "  pull_request:"
              "    branches: [ main ]"
              ""
              "jobs:"
              "  test:"
              "    runs-on: ubuntu-latest"
              "    steps:"
              "    - uses: actions/checkout@v3"
              "    - name: Setup .NET"
              "      uses: actions/setup-dotnet@v3"
              "      with:"
              "        dotnet-version: '8.0.x'"
              "    - name: Restore dependencies"
              "      run: dotnet restore"
              $"    - name: Run tests"
              $"      run: {testCommand}"
              $"    - name: Build"
              $"      run: {buildCommand}"
              ""
              "  deploy:"
              "    needs: test"
              "    runs-on: ubuntu-latest"
              "    if: github.ref == 'refs/heads/main'"
              "    steps:"
              "    - uses: actions/checkout@v3"
              "    - name: Deploy to production"
              "      run: echo \"Deployment step here\"" ]

        let workflow = String.concat "\n" workflowParts

        logInfo "CICDIntegrationManager" "GitHub Actions ワークフロー生成完了"
        workflow

    /// Docker Compose設定生成
    member _.GenerateDockerCompose(services: (string * string * (int * int) list) list) =
        let serviceConfigs =
            services
            |> List.map (fun (serviceName, imageName, ports) ->
                let portMappings =
                    ports
                    |> List.map (fun (host, container) -> sprintf "      - \"%d:%d\"" host container)
                    |> String.concat "\n"

                sprintf
                    "  %s:\n    image: %s\n    ports:\n%s\n    environment:\n      - NODE_ENV=production"
                    serviceName
                    imageName
                    portMappings)
            |> String.concat "\n\n"

        let dockerCompose =
            "version: '3.8'\n\n"
            + "services:\n"
            + serviceConfigs
            + "\n\n"
            + "networks:\n"
            + "  default:\n"
            + "    driver: bridge\n"

        logInfo "CICDIntegrationManager" "Docker Compose設定生成完了"
        dockerCompose

// ===============================================
// 統合開発フロー管理
// ===============================================

/// 統合開発フロー管理システム
type IntegratedDevFlowManager() =
    let gitManager = GitIntegrationManager()
    let dockerManager = DockerIntegrationManager()
    let cicdManager = CICDIntegrationManager()

    /// 開発環境セットアップ
    member this.SetupDevelopmentEnvironment (projectPath: string) (projectType: string) =
        // セキュリティ: Path Traversal対策
        let validateAndNormalizePath (basePath: string) (relativePath: string) =
            let fullPath = Path.Combine(basePath, relativePath)
            let normalizedPath = Path.GetFullPath(fullPath)
            let normalizedBase = Path.GetFullPath(basePath)

            if not (normalizedPath.StartsWith(normalizedBase)) then
                raise (ArgumentException($"Path traversal detected: {relativePath}"))

            normalizedPath

        try
            // GitHub Actions ワークフロー生成
            let workflow =
                cicdManager.GenerateGitHubActionsWorkflow projectType "dotnet test" "dotnet build"

            let workflowPath = validateAndNormalizePath projectPath ".github/workflows/ci.yml"

            Directory.CreateDirectory(Path.GetDirectoryName(workflowPath)) |> ignore
            File.WriteAllText(workflowPath, workflow)

            // Docker Compose生成
            let dockerCompose =
                cicdManager.GenerateDockerCompose
                    [ ("app", "fcode-app:latest", [ (8080, 80) ])
                      ("database", "postgres:13", [ (5432, 5432) ]) ]

            let composePath = validateAndNormalizePath projectPath "docker-compose.yml"
            File.WriteAllText(composePath, dockerCompose)

            logInfo "IntegratedDevFlowManager" "開発環境セットアップ完了"
            true
        with ex ->
            // セキュリティ: Information Disclosure対策
            logError "IntegratedDevFlowManager" "開発環境セットアップエラー"
            logDebug "IntegratedDevFlowManager" $"開発環境セットアップエラー詳細: {ex.Message}"
            false

    /// 統合デプロイフロー実行
    member this.ExecuteFullDeploymentFlow (projectPath: string) (commitMessage: string) (branchName: string) =
        try
            // Planning段階: Git状態確認
            logInfo "IntegratedDevFlowManager" "=== 計画段階: Git状態確認 ==="
            let gitStatus = gitManager.GetRepositoryStatus projectPath
            let planningResult = gitStatus.Success

            // TODO: 簡略化実装 - 実際のDevelopment/Testing/Building/Deployment/Monitoring実装を追加
            Map.ofList
                [ (Planning, planningResult)
                  (Development, true)
                  (Testing, true)
                  (Building, true)
                  (Deployment, true)
                  (Monitoring, true) ]

        with ex ->
            // セキュリティ: Information Disclosure対策
            logError "IntegratedDevFlowManager" "統合デプロイフロー実行エラー"
            logDebug "IntegratedDevFlowManager" $"統合デプロイフロー実行エラー詳細: {ex.Message}"

            Map.ofList
                [ (Planning, false)
                  (Development, false)
                  (Testing, false)
                  (Building, false)
                  (Deployment, false)
                  (Monitoring, false) ]
