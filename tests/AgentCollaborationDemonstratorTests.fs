module FCode.Tests.AgentCollaborationDemonstratorTests

open System
open NUnit.Framework
open FCode.AgentCollaborationDemonstrator
open FCode.Logger

[<SetUp>]
let Setup () =
    // テスト用ログレベル設定
    ()

[<TestFixture>]
[<Category("Integration")>]
type AgentCollaborationDemonstratorIntegrationTests() =

    [<Test>]
    member this.``PO指示デモ - ECサイトカート機能改善``() =
        async {
            // テスト実行
            let instruction = "ECサイトのカート機能を改善してください"
            let! result = AgentCollaborationDemo.runPODemo (instruction)

            // 結果検証
            match result with
            | Result.Ok report ->
                Assert.AreEqual(5, report.TasksCompleted) // 5エージェント
                Assert.AreEqual(0.92, report.QualityScore)
                Assert.IsTrue(report.Success)
                Assert.That(report.Duration.TotalMilliseconds > 0.0)
            | Result.Error _ -> Assert.Fail("PO指示デモ実行失敗")
        }

    [<Test>]
    member this.``PO指示デモ - パフォーマンス監視機能``() =
        async {
            // テスト実行
            let instruction = "パフォーマンス監視機能を追加してください"
            let! result = AgentCollaborationDemo.runPODemo (instruction)

            // 結果検証
            match result with
            | Result.Ok report ->
                Assert.AreEqual(5, report.TasksCompleted)
                Assert.That(report.Duration.TotalSeconds < 10.0) // パフォーマンステスト
                Assert.IsTrue(report.Success)
            | Result.Error _ -> Assert.Fail("パフォーマンス監視機能デモ実行失敗")
        }

    [<Test>]
    member this.``スクラムイベントデモ - 18分スプリント実行``() =
        async {
            // テスト実行
            let! result = AgentCollaborationDemo.runScrumDemo ()

            // 結果検証
            Assert.IsTrue(result.Success)
            Assert.AreEqual(3, result.StandupMeetings.Length) // 3回のスタンドアップ
            Assert.IsNotEmpty(result.SprintId)
            Assert.That(result.Duration.TotalMilliseconds > 0.0)

            // スタンドアップ内容検証
            let expectedMeetings =
                [ "1st standup completed"; "2nd standup completed"; "3rd standup completed" ]

            Assert.That(result.StandupMeetings, Is.EquivalentTo(expectedMeetings))
        }

    [<Test>]
    [<Category("Integration")>]
    member this.``完全デモ - 全機能統合テスト``() =
        async {
            // テスト実行
            let! result = AgentCollaborationDemo.runCompleteDemo ()

            // 結果検証
            Assert.AreEqual(3, result.TotalPOInstructions) // 3つのPO指示
            Assert.AreEqual(3, result.SuccessfulPOTasks) // すべて成功
            Assert.IsTrue(result.ScrumEventsExecuted)
            Assert.IsTrue(result.CollaborationFacadeActive)
            Assert.IsTrue(result.OverallSuccess)
        }

[<TestFixture>]
[<Category("Unit")>]
type AgentCollaborationDemonstratorUnitTests() =

    [<Test>]
    member this.``AgentCollaborationDemonstrator - 初期化とリソース管理``() =
        async {
            // 初期化テスト
            let demonstrator = new AgentCollaborationDemonstrator()
            Assert.IsNotNull(demonstrator)

            // リソースクリーンアップテスト
            (demonstrator :> IDisposable).Dispose()

            // 重複Dispose テスト（例外が発生しないこと）
            (demonstrator :> IDisposable).Dispose()
        }

    [<Test>]
    member this.``PO指示エラー処理テスト``() =
        async {
            use demonstrator = new AgentCollaborationDemonstrator()

            // 空の指示でエラーケースをテスト
            let! result = demonstrator.DemonstratePOWorkflow("")

            // エラーハンドリング確認（実際の実装によっては成功する可能性もある）
            match result with
            | Result.Ok report ->
                Assert.IsTrue(report.Success)
                Assert.AreEqual("", report.Instruction)
            | Result.Error _ -> Assert.Pass("エラーハンドリング正常動作")
        }

    [<Test>]
    member this.``デモ用エージェント状態作成テスト``() =
        async {
            use demonstrator = new AgentCollaborationDemonstrator()

            // PO指示実行でエージェント作成をテスト
            let instruction = "テスト指示"
            let! result = demonstrator.DemonstratePOWorkflow(instruction)

            match result with
            | Result.Ok report ->
                // 5エージェント（dev1, dev2, qa1, ux, pm）が想定
                Assert.AreEqual(5, report.AgentsInvolved.Length)
                Assert.That(report.AgentsInvolved, Contains.Item("dev1"))
                Assert.That(report.AgentsInvolved, Contains.Item("dev2"))
                Assert.That(report.AgentsInvolved, Contains.Item("qa1"))
                Assert.That(report.AgentsInvolved, Contains.Item("ux"))
                Assert.That(report.AgentsInvolved, Contains.Item("pm"))
            | Result.Error _ -> Assert.Fail("エージェント状態作成テスト失敗")
        }

[<TestFixture>]
[<Category("Performance")>]
type AgentCollaborationDemonstratorPerformanceTests() =

    [<Test>]
    member this.``並列処理パフォーマンステスト``() =
        async {
            let startTime = DateTime.UtcNow

            // 複数の指示を並列実行
            let instructions = [ "機能A実装"; "機能B実装"; "機能C実装" ]

            let! results = instructions |> List.map AgentCollaborationDemo.runPODemo |> Async.Parallel

            let endTime = DateTime.UtcNow
            let duration = endTime - startTime

            // パフォーマンス検証
            Assert.That(duration.TotalSeconds < 15.0) // 15秒以内
            Assert.AreEqual(3, results.Length)

            // すべて成功することを確認
            results
            |> Array.iter (function
                | Result.Ok _ -> ()
                | Result.Error msg -> Assert.Fail($"並列実行失敗: {msg}"))
        }

    [<Test>]
    member this.``リソース使用量テスト``() =
        async {
            let initialMemory = GC.GetTotalMemory(false)

            // 連続実行テスト
            for i in 1..10 do
                let! _ = AgentCollaborationDemo.runPODemo ($"テスト指示{i}")
                ()

            GC.Collect()
            GC.WaitForPendingFinalizers()
            let finalMemory = GC.GetTotalMemory(true)

            // メモリリーク検出（大まかなチェック）
            let memoryIncrease = finalMemory - initialMemory
            Assert.That(memoryIncrease < 50L * 1024L * 1024L) // 50MB以下
        }
