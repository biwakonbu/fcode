/// FC-027: JsonSanitizerテスト
/// 制御文字・エスケープシーケンス除去機能の包括的テスト
module FCode.Tests.JsonSanitizerTests

open System
open NUnit.Framework
open fcode
open FCode.Tests.CITestHelper
open FCode.Tests.MockUI

[<TestFixture>]
[<Category("Unit")>]
type JsonSanitizerTests() =

    [<SetUp>]
    member _.Setup() =
        // CI環境設定
        MockUITestSetup.setupMockUI ()

    [<TearDown>]
    member _.TearDown() = MockUITestSetup.cleanupMockUI ()

    // 基本的な制御文字除去テスト
    [<Test>]
    member _.``基本制御文字が正しく除去される``() =
        let input = "test\x00\x01\x02string"
        let result = JsonSanitizer.sanitizeForJson input
        Assert.AreEqual("test string", result)

    [<Test>]
    member _.``ANSIカラーコードが正しく除去される``() =
        let input = "test\u001b[31mred text\u001b[0mnormal"
        let result = JsonSanitizer.sanitizeForJson input
        Assert.AreEqual("test red text normal", result)

    [<Test>]
    member _.``Terminal.Gui制御シーケンスが除去される``() =
        let input = "content\u001b[?1003h\u001b[?1015h\u001b[?1006htest"
        let result = JsonSanitizer.sanitizeForJson input
        Assert.AreEqual("content test", result)

    [<Test>]
    member _.``複雑な制御文字組み合わせが除去される``() =
        let input = "\u001b[2J\u001b[H\u001b[?25lhidden\u001b[?25hvisible\u001b[K"
        let result = JsonSanitizer.sanitizeForJson input
        Assert.AreEqual("hidden visible", result)

    // JSON解析テスト
    [<Test>]
    member _.``有効なJSONが正しく解析される``() =
        let json = """{"status": "success", "content": "test"}"""
        let result = JsonSanitizer.tryParseJson<Map<string, obj>> json

        match result with
        | Ok _ -> Assert.Pass()
        | Error msg -> Assert.Fail($"Valid JSON parse failed: {msg}")

    [<Test>]
    member _.``制御文字入りJSONが正しく解析される``() =
        let dirtyJson =
            "{\x00\"status\": \u001b[31m\"success\"\u001b[0m, \"content\": \"test\"}"

        let result = JsonSanitizer.tryParseJson<Map<string, obj>> dirtyJson

        match result with
        | Ok _ -> Assert.Pass()
        | Error msg -> Assert.Fail($"Dirty JSON parse failed: {msg}")

    [<Test>]
    member _.``無効なJSONでエラーが返される``() =
        let invalidJson = "invalid json content"
        let result = JsonSanitizer.tryParseJson<Map<string, obj>> invalidJson

        match result with
        | Error _ -> Assert.Pass()
        | Ok _ -> Assert.Fail("Invalid JSON should return error")

    [<Test>]
    member _.``空文字列で適切なエラーが返される``() =
        let result = JsonSanitizer.tryParseJson<Map<string, obj>> ""

        match result with
        | Error msg when msg.Contains("Empty input") -> Assert.Pass()
        | Error msg -> Assert.Fail($"Unexpected error: {msg}")
        | Ok _ -> Assert.Fail("Empty string should return error")

    // JSON候補判定テスト
    [<Test>]
    member _.``有効なJSON構造が正しく判定される``() =
        Assert.IsTrue(JsonSanitizer.isValidJsonCandidate """{"key": "value"}""")
        Assert.IsTrue(JsonSanitizer.isValidJsonCandidate """[1, 2, 3]""")

    [<Test>]
    member _.``無効なJSON構造が正しく判定される``() =
        Assert.IsFalse(JsonSanitizer.isValidJsonCandidate "plain text")
        Assert.IsFalse(JsonSanitizer.isValidJsonCandidate "{incomplete")

    [<Test>]
    member _.``制御文字入りJSON構造が正しく判定される``() =
        let dirtyJson = "\u001b[31m{\"key\": \"value\"}\u001b[0m"
        Assert.IsTrue(JsonSanitizer.isValidJsonCandidate dirtyJson)

    // プレーンテキストサニタイズテスト
    [<Test>]
    member _.``プレーンテキストサニタイズが軽量動作する``() =
        let input = "test\u001b[31mcolored\u001b[0m text\x01control"
        let result = JsonSanitizer.sanitizeForPlainText input
        // 制御文字が空白で置換され、正規化される（実際の動作に基づく期待値）
        Assert.AreEqual("test [31mcolored [0m text control", result)

    // ログ出力テスト
    [<Test>]
    member _.``ログ付きJSON解析でログが出力される``() =
        let mutable logCalled = false
        let logFunc (msg: string) = logCalled <- true

        let dirtyJson = "\u001b[31m{\"status\": \"success\"}\u001b[0m"

        let result =
            JsonSanitizer.tryParseJsonWithLogging<Map<string, obj>> dirtyJson logFunc

        match result with
        | Ok _ -> Assert.IsTrue(logCalled, "Log function should be called for dirty input")
        | Error msg -> Assert.Fail($"Parse failed: {msg}")

    [<Test>]
    member _.``クリーンなJSON解析でログが出力されない``() =
        let mutable logCalled = false
        let logFunc (msg: string) = logCalled <- true

        let cleanJson = """{"status": "success"}"""

        let result =
            JsonSanitizer.tryParseJsonWithLogging<Map<string, obj>> cleanJson logFunc

        match result with
        | Ok _ -> Assert.IsFalse(logCalled, "Log function should not be called for clean input")
        | Error msg -> Assert.Fail($"Parse failed: {msg}")

    // エッジケーステスト
    [<Test>]
    member _.``null入力で適切に処理される``() =
        let result = JsonSanitizer.sanitizeForJson null
        Assert.AreEqual("", result)

    [<Test>]
    member _.``巨大文字列でパフォーマンス問題が発生しない``() =
        let largeInput = String.replicate 10000 "test\u001b[31mcolor\u001b[0m"
        let startTime = DateTime.Now
        let result = JsonSanitizer.sanitizeForJson largeInput
        let elapsed = DateTime.Now - startTime

        Assert.IsNotEmpty(result)
        Assert.Less(elapsed.TotalSeconds, 5.0, "Sanitization should complete within 5 seconds")

    // Terminal.Gui特化テスト
    [<Test>]
    member _.``Terminal.Gui特有制御シーケンスが完全除去される``() =
        let terminalOutput =
            "\u001b[?1049h\u001b[22;0;0t\u001b[1;24r"
            + "content"
            + "\u001b[?1049l\u001b[23;0;0t"

        let result = JsonSanitizer.sanitizeForJson terminalOutput
        Assert.AreEqual("content", result)

    [<Test>]
    member _.``マウス制御シーケンスが除去される``() =
        let mouseControl =
            "\u001b[?1003h\u001b[?1015h\u001b[?1006h"
            + "text"
            + "\u001b[?1003l\u001b[?1015l\u001b[?1006l"

        let result = JsonSanitizer.sanitizeForJson mouseControl
        Assert.AreEqual("text", result)

[<TestFixture>]
[<Category("Integration")>]
type JsonSanitizerIntegrationTests() =

    [<Test>]
    member _.``AgentCLI統合でJSON解析エラーが解決される``() =
        // CI環境で実際に発生したような制御文字入りJSON
        let problematicOutput =
            "\u001b[?1049h\u001b[22;0;0t"
            + """{"status": "success", "content": "result"}"""
            + "\u001b[?1049l"

        let result = JsonSanitizer.tryParseJson<Map<string, obj>> problematicOutput

        match result with
        | Ok _ -> Assert.Pass()
        | Error msg -> Assert.Fail($"Integration test failed: {msg}")

    [<Test>]
    member _.``SessionPersistence統合で状態読み込みエラーが解決される``() =
        // セッション状態JSON（制御文字汚染想定）
        let sessionJson =
            "\u001b[2J\u001b[H" + """{"sessionId": "test", "paneStates": {}}""" + "\u001b[K"

        let result = JsonSanitizer.sanitizeForJson sessionJson
        Assert.IsTrue(JsonSanitizer.isValidJsonCandidate result)
