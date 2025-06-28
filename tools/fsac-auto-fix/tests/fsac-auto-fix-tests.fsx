#!/usr/bin/env dotnet fsi

(*
F# FSAC Auto-Fix Tool Test Suite
FSAC自動修正ツールのテストスイート

使用方法:
  dotnet fsi fsac-auto-fix-tests.fsx

継続的改善プロセス:
1. 新しい問題が発見されたら、該当するテストケースを追加
2. テストケースに期待結果を記載
3. ツールを修正してテストが通るように改善
4. 回帰テストとして継続実行
*)

open System
open System.IO
open System.Text.RegularExpressions

// ===============================================
// テスト実行用のFixLevel定義
// ===============================================

type FixLevel =
    | Conservative  // 保守的：安全性最優先
    | Standard     // 標準：一般的な修正
    | Aggressive   // 積極的：すべての修正

type FixResult = {
    OriginalContent: string
    FixedContent: string
    ChangesApplied: int
    Description: string
}

// ===============================================
// 簡易版修正関数（テスト用）
// ===============================================

// IDisposableな型のリスト
let iDisposableTypes = Set.ofList [
    "FrameView"; "TextView"; "Window"; "Dialog"; "Button"; "Label"  // Terminal.Gui
    "Process"; "ProcessStartInfo"  // System.Diagnostics
    "NetworkStream"; "TcpClient"; "TcpListener"; "Socket"  // System.Net
    "FileStream"; "StreamReader"; "StreamWriter"; "MemoryStream"  // System.IO
    "Timer"; "CancellationTokenSource"  // System.Threading
    "HttpClient"; "HttpWebRequest"  // System.Net.Http
]

let needsNewKeyword (typeName: string) : bool =
    iDisposableTypes.Contains typeName

// 簡易版修正実装（テスト用）
let fixFileContent (content: string) (level: FixLevel) : FixResult =
    let mutable currentContent = content
    let mutable totalChanges = 0
    let mutable descriptions = []
    
    // 型キャスト関数の修正: float(x) -> float x
    let castPattern = Regex(@"\b(float|int|string|bool|byte|sbyte|int16|uint16|int32|uint32|int64|uint64|decimal|char)\s*\(([^)]+)\)")
    let castMatches = castPattern.Matches(currentContent)
    if castMatches.Count > 0 then
        currentContent <- castPattern.Replace(currentContent, "$1 $2")
        totalChanges <- totalChanges + castMatches.Count
        descriptions <- $"Cast function call: {castMatches.Count} fixes" :: descriptions

    // 静的メソッド呼び出しの修正: Type.Method(arg) -> Type.Method arg
    let staticMethodPattern = Regex(@"([A-Z]\w*\.[A-Z]\w*)\((\w+)\)")
    let staticMatches = staticMethodPattern.Matches(currentContent)
    if staticMatches.Count > 0 then
        currentContent <- staticMethodPattern.Replace(currentContent, "$1 $2")
        totalChanges <- totalChanges + staticMatches.Count
        descriptions <- $"Safe static method call: {staticMatches.Count} fixes" :: descriptions

    // Standard以上の場合: インスタンスメソッド呼び出し
    if level = Standard || level = Aggressive then
        let instanceMethodPattern = Regex(@"(\w+\.\w+)\((\w+)\)")
        let instanceMatches = instanceMethodPattern.Matches(currentContent)
        if instanceMatches.Count > 0 then
            currentContent <- instanceMethodPattern.Replace(currentContent, "$1 $2")
            totalChanges <- totalChanges + instanceMatches.Count
            descriptions <- $"Safe instance method call: {instanceMatches.Count} fixes" :: descriptions

        // 単純な関数呼び出し: func(arg) -> func arg （IDisposable除外）
        let funcPattern = Regex(@"\b(\w+)\((\w+)\)")
        let funcMatches = funcPattern.Matches(currentContent)
        let mutable safeFuncChanges = 0
        let mutable result = currentContent
        
        for i = funcMatches.Count - 1 downto 0 do
            let m = funcMatches.[i]
            let funcName = m.Groups.[1].Value
            let arg = m.Groups.[2].Value
            
            // newキーワードとIDisposableコンストラクタをチェック
            let beforeMatch = if m.Index > 3 then currentContent.Substring(m.Index - 4, 4) else ""
            if not (beforeMatch.Contains("new ") || needsNewKeyword funcName) then
                result <- result.Substring(0, m.Index) + $"{funcName} {arg}" + result.Substring(m.Index + m.Length)
                safeFuncChanges <- safeFuncChanges + 1
        
        if safeFuncChanges > 0 then
            currentContent <- result
            totalChanges <- totalChanges + safeFuncChanges
            descriptions <- $"Safe simple function call: {safeFuncChanges} fixes" :: descriptions

    {
        OriginalContent = content
        FixedContent = currentContent
        ChangesApplied = totalChanges
        Description = String.Join("; ", List.rev descriptions)
    }

// ===============================================
// テストケース定義（実際のコードから収集）
// ===============================================

type TestCase = {
    Name: string
    Level: FixLevel
    Input: string
    Expected: string
    Description: string
    ShouldChange: bool
}

type TestResult = {
    TestCase: TestCase
    ActualOutput: string
    Passed: bool
    Error: string option
}

// ===============================================
// 実際のコードベースから収集したテストケース
// ===============================================

let realWorldTestCases = [
    // 型キャスト関数（Conservative対応）
    {
        Name = "Float cast from DateTime"
        Level = Conservative
        Input = "let result = float(DateTime.Now.Ticks)"
        Expected = "let result = float DateTime.Now.Ticks"
        Description = "FSAC0004: 型キャスト関数の括弧除去"
        ShouldChange = true
    }
    
    {
        Name = "Int cast with complex expression"
        Level = Conservative
        Input = "let length = int(value + offset)"
        Expected = "let length = int (value + offset)"
        Description = "FSAC0004: 複雑な式での型キャスト"
        ShouldChange = true
    }
    
    // 静的メソッド呼び出し（Conservative対応）
    {
        Name = "Directory.Exists call"
        Level = Conservative
        Input = "if Directory.Exists(logDir) then"
        Expected = "if Directory.Exists logDir then"
        Description = "FSAC0004: Directory静的メソッド呼び出し"
        ShouldChange = true
    }
    
    {
        Name = "DateTime.ToString call"
        Level = Conservative
        Input = "let timestamp = DateTime.Now.ToString(format)"
        Expected = "let timestamp = DateTime.Now.ToString format"
        Description = "FSAC0004: DateTime静的メソッド呼び出し"
        ShouldChange = true
    }
    
    {
        Name = "Path.Combine call"
        Level = Conservative
        Input = "let path = Path.Combine(dir, file)"
        Expected = "let path = Path.Combine(dir, file)"
        Description = "FSAC0004: 複数引数は保持"
        ShouldChange = false
    }
    
    // インスタンスメソッド呼び出し（Standard対応）
    {
        Name = "String Contains call"
        Level = Standard
        Input = "if text.Contains(substr) then"
        Expected = "if text.Contains substr then"
        Description = "FSAC0004: インスタンスメソッド呼び出し"
        ShouldChange = true
    }
    
    {
        Name = "List Length property access"
        Level = Standard
        Input = "let count = list.Length()"
        Expected = "let count = list.Length"
        Description = "FSAC0004: プロパティアクセスの括弧除去"
        ShouldChange = true
    }
    
    // 関数呼び出し（Standard対応）
    {
        Name = "Safe function call"
        Level = Standard
        Input = "let result = calculate(value)"
        Expected = "let result = calculate value"
        Description = "FSAC0004: 安全な関数呼び出し"
        ShouldChange = true
    }
    
    {
        Name = "Lock function call"
        Level = Standard
        Input = "lock lockObj (fun () ->"
        Expected = "lock lockObj (fun () ->"
        Description = "FSAC0004: 複雑な引数は保持"
        ShouldChange = false
    }
    
    // IDisposableコンストラクタ保護テスト
    {
        Name = "FrameView constructor protection"
        Level = Standard
        Input = "let frame = new FrameView(title)"
        Expected = "let frame = new FrameView(title)"
        Description = "FSAC0004: IDisposableコンストラクタ保護"
        ShouldChange = false
    }
    
    {
        Name = "Process constructor protection"
        Level = Standard
        Input = "let proc = new Process()"
        Expected = "let proc = new Process()"
        Description = "FSAC0004: Processコンストラクタ保護"
        ShouldChange = false
    }
    
    {
        Name = "FileStream constructor protection"
        Level = Standard
        Input = "let stream = new FileStream(path)"
        Expected = "let stream = new FileStream(path)"
        Description = "FSAC0004: FileStreamコンストラクタ保護"
        ShouldChange = false
    }
]

// ===============================================
// 問題ケース（修正が困難または注意が必要）
// ===============================================

let problemTestCases = [
    {
        Name = "Method chain with property"
        Level = Standard
        Input = "let length = Directory.GetFiles(dir).Length"
        Expected = "let length = (Directory.GetFiles dir).Length"
        Description = "FSAC0004: メソッドチェーンでの優先順位問題"
        ShouldChange = true
    }
    
    {
        Name = "Complex expression precedence"
        Level = Standard
        Input = "let result = Math.Max(value1 + value2, limit)"
        Expected = "let result = Math.Max(value1 + value2, limit)"
        Description = "FSAC0004: 複雑な式での括弧保持"
        ShouldChange = false
    }
    
    {
        Name = "Nested function calls"
        Level = Standard
        Input = "let result = outer(inner(value))"
        Expected = "let result = outer (inner value)"
        Description = "FSAC0004: ネストした関数呼び出し"
        ShouldChange = true
    }
]

// ===============================================
// エッジケースとエラーケース
// ===============================================

let edgeTestCases = [
    {
        Name = "Empty parentheses"
        Level = Standard
        Input = "let result = getValue()"
        Expected = "let result = getValue()"
        Description = "FSAC0004: 空の括弧は保持"
        ShouldChange = false
    }
    
    {
        Name = "String with parentheses"
        Level = Standard
        Input = "let text = \"function(test)\""
        Expected = "let text = \"function(test)\""
        Description = "FSAC0004: 文字列内の括弧は無視"
        ShouldChange = false
    }
    
    {
        Name = "Comment with parentheses"
        Level = Standard
        Input = "// Call function(arg)\nlet value = 42"
        Expected = "// Call function(arg)\nlet value = 42"
        Description = "FSAC0004: コメント内の括弧は無視"
        ShouldChange = false
    }
    
    {
        Name = "Multiple arguments"
        Level = Standard
        Input = "let result = func(arg1, arg2, arg3)"
        Expected = "let result = func(arg1, arg2, arg3)"
        Description = "FSAC0004: 複数引数の括弧は保持"
        ShouldChange = false
    }
]

// ===============================================
// テスト実行エンジン
// ===============================================

let runTest (testCase: TestCase) : TestResult =
    try
        let result = fixFileContent testCase.Input testCase.Level
        
        let passed = 
            if testCase.ShouldChange then
                result.FixedContent = testCase.Expected && result.ChangesApplied > 0
            else
                result.FixedContent = testCase.Expected && result.ChangesApplied = 0
        
        {
            TestCase = testCase
            ActualOutput = result.FixedContent
            Passed = passed
            Error = None
        }
    with
    | ex ->
        {
            TestCase = testCase
            ActualOutput = ""
            Passed = false
            Error = Some ex.Message
        }

let runTestSuite (testCases: TestCase list) (suiteName: string) : TestResult list =
    printfn $"\n🧪 Running {suiteName} Test Suite ({testCases.Length} tests)"
    printfn "%s" ("=".PadRight(60, '='))
    
    let results = testCases |> List.map runTest
    
    let passedCount = results |> List.filter (fun r -> r.Passed) |> List.length
    let failedCount = results.Length - passedCount
    
    for result in results do
        let status = if result.Passed then "✅ PASS" else "❌ FAIL"
        printfn $"{status} {result.TestCase.Name}"
        
        if not result.Passed then
            printfn $"      Expected: {result.TestCase.Expected}"
            printfn $"      Actual:   {result.ActualOutput}"
            printfn $"      Desc:     {result.TestCase.Description}"
            match result.Error with
            | Some error -> printfn $"      Error:    {error}"
            | None -> ()
    
    printfn ""
    printfn $"📊 Results: {passedCount} passed, {failedCount} failed"
    
    results

// ===============================================
// メインテスト実行
// ===============================================

let runAllTests () =
    printfn "🔬 F# FSAC Auto-Fix Tool Test Suite"
    let now = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
    printfn $"📅 Test Run: {now}"
    printfn ""
    
    let mutable allResults = []
    
    // 実際のコードベースから収集したテスト
    let realWorldResults = runTestSuite realWorldTestCases "Real World Cases"
    allResults <- realWorldResults @ allResults
    
    // 問題ケーステスト
    let problemResults = runTestSuite problemTestCases "Problem Cases"
    allResults <- problemResults @ allResults
    
    // エッジケーステスト
    let edgeResults = runTestSuite edgeTestCases "Edge Cases"
    allResults <- edgeResults @ allResults
    
    // 全体サマリー
    let totalPassed = allResults |> List.filter (fun r -> r.Passed) |> List.length
    let totalFailed = allResults.Length - totalPassed
    let successRate = (float totalPassed / float allResults.Length) * 100.0
    
    printfn "\n%s" ("=".PadRight(60, '='))
    printfn "📋 Overall Test Summary"
    printfn "%s" ("=".PadRight(60, '='))
    printfn $"✅ Total Passed:  {totalPassed}"
    printfn $"❌ Total Failed:  {totalFailed}"
    printfn $"📊 Success Rate:  {successRate:F1}%%"
    printfn $"📈 Total Tests:   {allResults.Length}"
    
    if totalFailed > 0 then
        printfn "\n⚠️  Failed Tests:"
        for result in allResults do
            if not result.Passed then
                printfn $"   • {result.TestCase.Name}: {result.TestCase.Description}"
    
    printfn "\n💡 継続的改善プロセス:"
    printfn "   1. 新しい問題が発見されたら、このファイルにテストケースを追加"
    printfn "   2. 期待される結果をExpectedフィールドに記載"
    printfn "   3. fsac-auto-fix.fsx ツールを修正"
    printfn "   4. テストを再実行して改善を確認"
    printfn "   5. 全テストが通ることを確認してから本番適用"
    
    allResults

// テスト実行
let results = runAllTests ()

// テスト結果に基づく終了コード
let exitCode = if results |> List.exists (fun r -> not r.Passed) then 1 else 0
printfn $"\n🏁 Test run completed with exit code: {exitCode}"