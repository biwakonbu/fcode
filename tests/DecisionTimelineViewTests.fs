module FCode.Tests.DecisionTimelineViewTests

open System
open System.Threading
open NUnit.Framework
open Terminal.Gui
open FCode.DecisionTimelineView
open FCode.AgentMessaging
open FCode.Logger

[<TestFixture>]
type DecisionTimelineViewTests() =


    [<Test>]
    [<Category("Unit")>]
    member _.``DecisionTimelineManager Basic Creation Test``() =
        // 意思決定タイムライン管理の基本生成テスト
        let manager = new DecisionTimelineManager()

        Assert.AreEqual(0, manager.GetDecisionCount())
        Assert.AreEqual(0, manager.GetAllDecisions().Length)

    [<Test>]
    [<Category("Unit")>]
    member _.``StartDecision Basic Test``() =
        // 意思決定開始基本テスト
        let manager = new DecisionTimelineManager()

        let decisionId =
            manager.StartDecision("Test Decision", "Test description", High, [ "agent1"; "agent2" ])

        Assert.AreEqual(1, manager.GetDecisionCount())

        let decisions = manager.GetAllDecisions()
        Assert.AreEqual(1, decisions.Length)
        Assert.AreEqual("Test Decision", decisions.[0].Title)
        Assert.AreEqual("Test description", decisions.[0].Description)
        Assert.AreEqual(Problem, decisions.[0].Stage)
        Assert.AreEqual(High, decisions.[0].Priority)
        Assert.AreEqual([ "agent1"; "agent2" ], decisions.[0].Stakeholders)
        Assert.AreEqual("active", decisions.[0].Status)

    [<Test>]
    [<Category("Unit")>]
    member _.``UpdateDecisionStage Test``() =
        // 意思決定段階更新テスト
        let manager = new DecisionTimelineManager()

        let decisionId =
            manager.StartDecision("Test Decision", "Test description", Normal, [ "agent1" ])

        let updateResult =
            manager.UpdateDecisionStage(decisionId, Analysis, "agent1", "Analysis completed")

        Assert.IsTrue(updateResult)

        let decisions = manager.GetAllDecisions()
        Assert.AreEqual(1, decisions.Length)
        Assert.AreEqual(Analysis, decisions.[0].Stage)

    [<Test>]
    [<Category("Unit")>]
    member _.``CompleteDecision Test``() =
        // 意思決定完了テスト
        let manager = new DecisionTimelineManager()

        let decisionId =
            manager.StartDecision("Test Decision", "Test description", Normal, [ "agent1" ])

        let completeResult =
            manager.CompleteDecision(decisionId, "agent1", "Final decision made")

        Assert.IsTrue(completeResult)

        let decisions = manager.GetAllDecisions()
        Assert.AreEqual(1, decisions.Length)
        Assert.AreEqual(Review, decisions.[0].Stage)
        Assert.AreEqual("completed", decisions.[0].Status)
        Assert.IsTrue(snd decisions.[0].Timeline |> Option.isSome)

    [<Test>]
    [<Category("Unit")>]
    member _.``GetActiveDecisions Filter Test``() =
        // アクティブ意思決定フィルタテスト
        let manager = new DecisionTimelineManager()

        let decisionId1 =
            manager.StartDecision("Active Decision 1", "Description 1", High, [ "agent1" ])

        let decisionId2 =
            manager.StartDecision("Active Decision 2", "Description 2", Normal, [ "agent2" ])

        let decisionId3 =
            manager.StartDecision("Completed Decision", "Description 3", Low, [ "agent3" ])

        manager.CompleteDecision(decisionId3, "agent3", "Completed") |> ignore

        let activeDecisions = manager.GetActiveDecisions()
        let allDecisions = manager.GetAllDecisions()

        Assert.AreEqual(3, allDecisions.Length)
        Assert.AreEqual(2, activeDecisions.Length)
        Assert.IsTrue(activeDecisions |> Array.forall (fun d -> d.Status = "active"))

    [<Test>]
    [<Category("Unit")>]
    member _.``ProcessDecisionMessage Escalation Test``() =
        // エスカレーションメッセージ処理テスト
        let manager = new DecisionTimelineManager()

        let escalationMessage =
            MessageBuilder()
                .From("dev1")
                .To("PM")
                .OfType(MessageType.Escalation)
                .WithPriority(Critical)
                .WithContent("Critical issue requires immediate decision")
                .WithMetadata("decision_title", "Critical Issue Resolution")
                .WithMetadata("stakeholders", "PM,dev1,qa1")
                .Build()

        manager.ProcessDecisionMessage(escalationMessage)

        let decisions = manager.GetAllDecisions()
        Assert.AreEqual(1, decisions.Length)
        Assert.AreEqual("Critical Issue Resolution", decisions.[0].Title)
        Assert.AreEqual(Critical, decisions.[0].Priority)
        Assert.AreEqual([ "PM"; "dev1"; "qa1" ], decisions.[0].Stakeholders)

    [<Test>]
    [<Category("Unit")>]
    member _.``ProcessDecisionMessage Collaboration Test``() =
        // 協調メッセージ処理テスト
        let manager = new DecisionTimelineManager()

        let decisionId =
            manager.StartDecision("Test Decision", "Test description", Normal, [ "agent1" ])

        let collaborationMessage =
            MessageBuilder()
                .From("agent1")
                .To("agent2")
                .OfType(MessageType.Collaboration)
                .WithPriority(Normal)
                .WithContent("Analysis phase completed")
                .WithMetadata("decision_id", decisionId)
                .WithMetadata("decision_stage", "analysis")
                .Build()

        manager.ProcessDecisionMessage(collaborationMessage)

        let decisions = manager.GetAllDecisions()
        Assert.AreEqual(1, decisions.Length)
        Assert.AreEqual(Analysis, decisions.[0].Stage)

    [<Test>]
    [<Category("Unit")>]
    member _.``DecisionStage Display Format Test``() =
        // 意思決定段階表示フォーマットテスト
        let manager = new DecisionTimelineManager()

        // 各段階の意思決定を作成
        let stages =
            [ Problem; Analysis; Options; Evaluation; Decision; Implementation; Review ]

        stages
        |> List.iteri (fun i stage ->
            let decisionId =
                manager.StartDecision($"Decision {i}", $"Description {i}", Normal, [ "agent1" ])

            if stage <> Problem then
                manager.UpdateDecisionStage(decisionId, stage, "agent1", $"Updated to {stage}")
                |> ignore)

        Assert.AreEqual(stages.Length, manager.GetDecisionCount())

        let decisions = manager.GetAllDecisions()
        Assert.AreEqual(stages.Length, decisions.Length)

        // 各段階が正しく設定されていることを確認
        stages
        |> List.iter (fun expectedStage -> Assert.IsTrue(decisions |> Array.exists (fun d -> d.Stage = expectedStage)))

    [<Test>]
    [<Category("Unit")>]
    member _.``GetDecisionDetail Test``() =
        // 意思決定詳細取得テスト
        let manager = new DecisionTimelineManager()

        let decisionId =
            manager.StartDecision("Test Decision", "Test description", High, [ "agent1"; "agent2" ])

        let (found, decision) = manager.GetDecisionDetail(decisionId)
        Assert.IsTrue(found)
        Assert.AreEqual("Test Decision", decision.Title)
        Assert.AreEqual("Test description", decision.Description)
        Assert.AreEqual(High, decision.Priority)

        let (notFound, _) = manager.GetDecisionDetail("nonexistent-id")
        Assert.IsFalse(notFound)

    [<Test>]
    [<Category("Unit")>]
    member _.``ClearHistory Test``() =
        // 履歴クリアテスト
        let manager = new DecisionTimelineManager()

        let decisionId1 =
            manager.StartDecision("Decision 1", "Description 1", High, [ "agent1" ])

        let decisionId2 =
            manager.StartDecision("Decision 2", "Description 2", Normal, [ "agent2" ])

        manager.UpdateDecisionStage(decisionId1, Analysis, "agent1", "Analysis completed")
        |> ignore

        Assert.AreEqual(2, manager.GetDecisionCount())

        manager.ClearHistory()

        // 意思決定は残り、履歴のみクリアされる
        Assert.AreEqual(2, manager.GetDecisionCount())

    [<Test>]
    [<Category("Integration")>]
    member _.``Global DecisionTimelineManager Usage Test``() =
        // グローバル意思決定タイムライン管理使用テスト
        let initialCount = globalDecisionTimelineManager.GetDecisionCount()

        let decisionId =
            startDecision "Global Decision Test" "Global decision description" High [ "global-agent1"; "global-agent2" ]

        let newCount = globalDecisionTimelineManager.GetDecisionCount()
        Assert.AreEqual(initialCount + 1, newCount)

        let decisions = globalDecisionTimelineManager.GetAllDecisions()
        let latestDecision = decisions |> Array.maxBy (fun d -> fst d.Timeline)
        Assert.AreEqual("Global Decision Test", latestDecision.Title)
        Assert.AreEqual(High, latestDecision.Priority)

    [<Test>]
    [<Category("Integration")>]
    member _.``Global Functions Integration Test``() =
        // グローバル関数統合テスト
        let decisionId =
            startDecision "Integration Test Decision" "Test integration" Normal [ "agent1" ]

        let updateResult = updateDecisionStage decisionId Analysis "agent1" "Analysis phase"
        Assert.IsTrue(updateResult)

        let completeResult = completeDecision decisionId "agent1" "Final decision"
        Assert.IsTrue(completeResult)

        let (found, decision) = globalDecisionTimelineManager.GetDecisionDetail(decisionId)
        Assert.IsTrue(found)
        Assert.AreEqual(Review, decision.Stage)
        Assert.AreEqual("completed", decision.Status)
