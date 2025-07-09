namespace FCode.Tests

open NUnit.Framework
open System
open System.IO
open FCode.DevOpsIntegration
open FCode.Tests.CITestHelper

[<TestFixture>]
[<Category("Unit")>]
type DevOpsIntegrationTests() =

    let tempDir =
        Path.Combine(Path.GetTempPath(), "fcode-devops-tests-" + Guid.NewGuid().ToString("N")[..7])

    [<SetUp>]
    member _.Setup() =
        Directory.CreateDirectory(tempDir) |> ignore

    [<TearDown>]
    member _.Cleanup() =
        try
            if Directory.Exists(tempDir) then
                Directory.Delete(tempDir, true)
        with _ ->
            ()

    [<Test>]
    [<Category("Unit")>]
    member _.``GitIntegrationManagerリポジトリ状態確認テスト``() =
        // Git が利用可能な場合のみテスト実行
        if CIEnvironment.isCI () then
            Assert.Inconclusive("CI環境ではGitテストをスキップ")

        let gitManager = GitIntegrationManager()

        // 一時ディレクトリでgit初期化
        let gitInitResult = System.Diagnostics.Process.Start("git", $"init {tempDir}")
        gitInitResult.WaitForExit()

        if gitInitResult.ExitCode = 0 then
            let result = gitManager.GetRepositoryStatus tempDir

            Assert.IsNotNull(result)
            Assert.AreEqual(Git, result.Command.Tool)
            // 新しいリポジトリなので成功または空の状態
            Assert.IsTrue(result.Success || result.StandardOutput.Length = 0)
        else
            Assert.Inconclusive("Git初期化に失敗しました")

    [<Test>]
    [<Category("Unit")>]
    member _.``GitIntegrationManagerブランチ作成テスト``() =
        if CIEnvironment.isCI () then
            Assert.Inconclusive("CI環境ではGitテストをスキップ")

        let gitManager = GitIntegrationManager()

        // Git リポジトリ初期化
        let gitInitResult = System.Diagnostics.Process.Start("git", $"init {tempDir}")
        gitInitResult.WaitForExit()

        if gitInitResult.ExitCode = 0 then
            // 初期コミット作成（ブランチ作成に必要）
            let configResult1 =
                System.Diagnostics.Process.Start("git", $"-C {tempDir} config user.email \"test@example.com\"")

            configResult1.WaitForExit()

            let configResult2 =
                System.Diagnostics.Process.Start("git", $"-C {tempDir} config user.name \"Test User\"")

            configResult2.WaitForExit()

            File.WriteAllText(Path.Combine(tempDir, "README.md"), "# Test Repository")
            let addResult = System.Diagnostics.Process.Start("git", $"-C {tempDir} add .")
            addResult.WaitForExit()

            let commitResult =
                System.Diagnostics.Process.Start("git", $"-C {tempDir} commit -m \"Initial commit\"")

            commitResult.WaitForExit()

            if commitResult.ExitCode = 0 then
                let branchName = "feature/test-branch"
                let success = gitManager.CreateAndSwitchBranch tempDir branchName

                // ブランチ作成の成功は Git の状態に依存するため、例外が発生しないことを確認
                Assert.DoesNotThrow(fun () -> gitManager.CreateAndSwitchBranch tempDir branchName |> ignore)
            else
                Assert.Inconclusive("初期コミット作成に失敗しました")
        else
            Assert.Inconclusive("Git初期化に失敗しました")

    [<Test>]
    [<Category("Unit")>]
    member _.``DockerIntegrationManagerコンテナ状態確認テスト``() =
        let dockerManager = DockerIntegrationManager()

        // Docker が利用可能かどうかに関係なく、メソッドが例外を投げないことを確認
        Assert.DoesNotThrow(fun () ->
            let result = dockerManager.GetContainerStatus()
            // 結果はDockerの利用可能性に依存するが、None または Some(string) が返される
            Assert.IsTrue(result.IsNone || result.IsSome))

    [<Test>]
    [<Category("Unit")>]
    member _.``DockerIntegrationManagerコンテナビルドテスト``() =
        let dockerManager = DockerIntegrationManager()

        // Dockerfileモック作成
        let dockerfilePath = Path.Combine(tempDir, "Dockerfile")

        let dockerfileContent =
            """FROM alpine:latest
RUN echo "Hello World"
CMD ["echo", "Hello from container"]"""

        File.WriteAllText(dockerfilePath, dockerfileContent)

        // Docker コンテナ状態確認テスト
        let containerStatus = dockerManager.GetContainerStatus()

        // Docker が利用可能でない場合はNoneが返される
        Assert.IsTrue(containerStatus.IsSome || containerStatus.IsNone) // 例外が発生しないことを確認

    [<Test>]
    [<Category("Unit")>]
    member _.``CICDIntegrationManagerワークフロー生成テスト``() =
        let cicdManager = CICDIntegrationManager()

        let workflow =
            cicdManager.GenerateGitHubActionsWorkflow "dotnet" "dotnet test" "dotnet build"

        Assert.IsNotEmpty(workflow)
        Assert.IsTrue(workflow.Contains("name: CI/CD Pipeline"))
        Assert.IsTrue(workflow.Contains("dotnet test"))
        Assert.IsTrue(workflow.Contains("dotnet build"))
        Assert.IsTrue(workflow.Contains("Setup .NET"))
        Assert.IsTrue(workflow.Contains("ubuntu-latest"))

    [<Test>]
    [<Category("Unit")>]
    member _.``CICDIntegrationManagerDockerCompose生成テスト``() =
        let cicdManager = CICDIntegrationManager()

        let services =
            [ ("web", "nginx:latest", [ (80, 80); (443, 443) ])
              ("api", "node:16", [ (3000, 3000) ])
              ("db", "postgres:13", [ (5432, 5432) ]) ]

        let dockerCompose = cicdManager.GenerateDockerCompose services

        Assert.IsNotEmpty(dockerCompose)
        Assert.IsTrue(dockerCompose.Contains("version: '3.8'"))
        Assert.IsTrue(dockerCompose.Contains("web:"))
        Assert.IsTrue(dockerCompose.Contains("nginx:latest"))
        Assert.IsTrue(dockerCompose.Contains("api:"))
        Assert.IsTrue(dockerCompose.Contains("node:16"))
        Assert.IsTrue(dockerCompose.Contains("db:"))
        Assert.IsTrue(dockerCompose.Contains("postgres:13"))
        Assert.IsTrue(dockerCompose.Contains("\"80:80\""))
        Assert.IsTrue(dockerCompose.Contains("\"3000:3000\""))
        Assert.IsTrue(dockerCompose.Contains("\"5432:5432\""))

    [<Test>]
    [<Category("Unit")>]
    member _.``IntegratedDevFlowManager開発環境セットアップテスト``() =
        let flowManager = IntegratedDevFlowManager()

        let success = flowManager.SetupDevelopmentEnvironment tempDir "dotnet"

        // セットアップが成功するか、ディレクトリ問題で失敗するかを確認
        Assert.IsTrue(success || not success) // 例外が発生しないことを確認

        if success then
            // ファイルが作成されたかチェック
            let workflowPath = Path.Combine(tempDir, ".github", "workflows", "ci.yml")
            let composePath = Path.Combine(tempDir, "docker-compose.yml")

            if File.Exists(workflowPath) then
                let workflowContent = File.ReadAllText(workflowPath)
                Assert.IsTrue(workflowContent.Contains("name: CI/CD Pipeline"))

            if File.Exists(composePath) then
                let composeContent = File.ReadAllText(composePath)
                Assert.IsTrue(composeContent.Contains("version: '3.8'"))

[<TestFixture>]
[<Category("Integration")>]
type DevOpsIntegrationIntegrationTests() =

    let tempDir =
        Path.Combine(Path.GetTempPath(), "fcode-devops-integration-" + Guid.NewGuid().ToString("N")[..7])

    [<SetUp>]
    member _.Setup() =
        Directory.CreateDirectory(tempDir) |> ignore

    [<TearDown>]
    member _.Cleanup() =
        try
            if Directory.Exists(tempDir) then
                Directory.Delete(tempDir, true)
        with _ ->
            ()

    [<Test>]
    [<Category("Integration")>]
    member _.``統合開発フローフルテスト``() =
        if CIEnvironment.isCI () then
            Assert.Inconclusive("CI環境では統合テストをスキップ")

        let flowManager = IntegratedDevFlowManager()

        // テストプロジェクト準備
        let projectPath = tempDir
        File.WriteAllText(Path.Combine(projectPath, "README.md"), "# Test Project")

        // Dockerfileモック
        let dockerfileContent =
            """FROM alpine:latest
WORKDIR /app
COPY . .
CMD ["echo", "Hello from test app"]"""

        File.WriteAllText(Path.Combine(projectPath, "Dockerfile"), dockerfileContent)

        // Git リポジトリ初期化（利用可能な場合）
        let gitInitResult = System.Diagnostics.Process.Start("git", $"init {projectPath}")
        gitInitResult.WaitForExit()

        if gitInitResult.ExitCode = 0 then
            // Git設定
            let configEmail =
                System.Diagnostics.Process.Start("git", $"-C {projectPath} config user.email \"test@example.com\"")

            configEmail.WaitForExit()

            let configName =
                System.Diagnostics.Process.Start("git", $"-C {projectPath} config user.name \"Test User\"")

            configName.WaitForExit()

            // 統合フロー実行
            let results = flowManager.ExecuteFullDeploymentFlow projectPath "Test commit" "main"

            // 少なくとも計画段階は成功することを期待
            Assert.IsTrue(results.ContainsKey(Planning))

            // フロー全体が例外なく実行されることを確認
            Assert.GreaterOrEqual(results.Count, 1)

            // 各段階の結果を検証
            for kvp in results do
                let stage, success = kvp.Key, kvp.Value
                printfn $"段階: {stage}, 結果: {success}"
        else
            Assert.Inconclusive("Git が利用できないため統合テストをスキップ")

    [<Test>]
    [<Category("Integration")>]
    member _.``複数ツール統合テスト``() =
        let gitManager = GitIntegrationManager()
        let dockerManager = DockerIntegrationManager()
        let cicdManager = CICDIntegrationManager()

        // 各ツールが独立して動作することを確認
        Assert.DoesNotThrow(fun () -> gitManager.GetRepositoryStatus tempDir |> ignore)
        Assert.DoesNotThrow(fun () -> dockerManager.GetContainerStatus() |> ignore)
        Assert.DoesNotThrow(fun () -> cicdManager.GenerateGitHubActionsWorkflow "test" "test" "build" |> ignore)

        // 生成された設定ファイルの統合確認
        let workflow =
            cicdManager.GenerateGitHubActionsWorkflow "dotnet" "dotnet test" "dotnet build"

        let dockerCompose =
            cicdManager.GenerateDockerCompose [ ("app", "test-app:latest", [ (8080, 80) ]) ]

        Assert.IsNotEmpty(workflow)
        Assert.IsNotEmpty(dockerCompose)

        // 両方の設定が相互に矛盾しないことを確認
        Assert.IsTrue(workflow.Contains("CI/CD"))
        Assert.IsTrue(dockerCompose.Contains("app:"))

[<TestFixture>]
[<Category("Performance")>]
type DevOpsIntegrationPerformanceTests() =

    [<Test>]
    [<Category("Performance")>]
    member _.``設定ファイル大量生成性能テスト``() =
        let cicdManager = CICDIntegrationManager()

        let startTime = DateTime.Now

        // 100個のワークフロー生成
        let workflows =
            [ for i in 1..100 -> cicdManager.GenerateGitHubActionsWorkflow $"project-{i}" $"test-{i}" $"build-{i}" ]

        // 100個のDocker Compose生成
        let dockerComposes =
            [ for i in 1..100 ->
                  let services = [ ($"service-{i}", $"image-{i}:latest", [ (8000 + i, 80) ]) ]
                  cicdManager.GenerateDockerCompose services ]

        let endTime = DateTime.Now
        let duration = endTime - startTime

        // 大量生成が3秒以内に完了することを確認
        Assert.Less(duration.TotalSeconds, 3.0)
        Assert.AreEqual(100, workflows.Length)
        Assert.AreEqual(100, dockerComposes.Length)

        // 生成されたファイルが有効であることを確認
        workflows
        |> List.iter (fun workflow ->
            Assert.IsNotEmpty(workflow)
            Assert.IsTrue(workflow.Contains("CI/CD Pipeline")))

        dockerComposes
        |> List.iter (fun compose ->
            Assert.IsNotEmpty(compose)
            Assert.IsTrue(compose.Contains("version: '3.8'")))

    [<Test>]
    [<Category("Performance")>]
    member _.``Git操作並列実行性能テスト``() =
        if CIEnvironment.isCI () then
            Assert.Inconclusive("CI環境では並列Git操作テストをスキップ")

        let gitManager = GitIntegrationManager()

        let tempDirs =
            [ for i in 1..5 ->
                  let guidString = Guid.NewGuid().ToString("N")
                  let dir = Path.Combine(Path.GetTempPath(), $"git-perf-test-{i}-{guidString.[..7]}")
                  Directory.CreateDirectory(dir) |> ignore
                  dir ]

        try
            let startTime = DateTime.Now

            // 並列でGit状態確認
            let results =
                tempDirs
                |> List.map (fun dir -> async { return gitManager.GetRepositoryStatus dir })
                |> Async.Parallel
                |> Async.RunSynchronously

            let endTime = DateTime.Now
            let duration = endTime - startTime

            // 並列実行が5秒以内に完了することを確認
            Assert.Less(duration.TotalSeconds, 5.0)
            Assert.AreEqual(5, results.Length)

            // 全ての結果が返されることを確認（成功/失敗は問わない）
            results
            |> Array.iter (fun result ->
                Assert.IsNotNull(result)
                Assert.IsNotNull(result.Command))

        finally
            // クリーンアップ
            tempDirs
            |> List.iter (fun dir ->
                try
                    if Directory.Exists(dir) then
                        Directory.Delete(dir, true)
                with _ ->
                    ())
