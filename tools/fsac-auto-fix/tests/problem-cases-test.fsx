#!/usr/bin/env dotnet fsi

open System
open System.Text.RegularExpressions

// 実際のコードベースから発見された問題ケース
let problemCases = [
    // メソッドチェーンでの優先順位問題
    ("let random = Random().Next(1000, 9999)", "let random = (Random()).Next(1000, 9999)", "Random constructor + method chain")
    ("let hash = filePath.GetHashCode().ToString(\"X8\")", "let hash = (filePath.GetHashCode()).ToString \"X8\"", "Method chain with property")
    ("let id = Guid.NewGuid().ToString()", "let id = (Guid.NewGuid()).ToString()", "Static method + instance method")
    
    // 配列・プロパティアクセスでの問題
    ("let files = Directory.GetFiles(dir).Length", "let files = (Directory.GetFiles dir).Length", "Array method with property")
    ("let count = list.Count()", "let count = list.Count", "Property access with empty parentheses")
    
    // 複雑な式での括弧の必要性
    ("let result = Math.Max(value1 + value2, limit)", "let result = Math.Max(value1 + value2, limit)", "Complex expressions should keep parentheses")
    ("let path = Path.Combine(dir, file)", "let path = Path.Combine(dir, file)", "Multiple arguments should keep parentheses")
    
    // IDisposableコンストラクタ保護の確認
    ("let view = new TextView(text)", "let view = new TextView(text)", "IDisposable constructor protection")
    ("let stream = new FileStream(path)", "let stream = new FileStream(path)", "FileStream constructor protection")
    
    // 型キャストでの複雑な式
    ("let value = float(Math.Abs(x))", "let value = float (Math.Abs x)", "Type cast with method call")
    ("let result = int(value + offset)", "let result = int (value + offset)", "Type cast with complex expression")
]

// 現在のツールの簡易版模擬
let currentToolSimulation (input: string) : string =
    let mutable result = input
    
    // 1. 型キャスト関数の修正
    let castPattern = Regex(@"\b(float|int|string|bool|byte|sbyte|int16|uint16|int32|uint32|int64|uint64|decimal|char)\s*\(([^)]+)\)")
    result <- castPattern.Replace(result, "$1 $2")
    
    // 2. 静的メソッド呼び出しの修正
    let staticMethodPattern = Regex(@"([A-Z]\w*\.[A-Z]\w*)\(([^)]+)\)")
    result <- staticMethodPattern.Replace(result, "$1 $2")
    
    // 3. インスタンスメソッド呼び出しの修正
    let instanceMethodPattern = Regex(@"(\w+\.\w+)\(([^)]*)\)")
    result <- instanceMethodPattern.Replace(result, "$1 $2")
    
    // 4. 単純な関数呼び出しの修正（IDisposable除外）
    let funcPattern = Regex(@"\b(\w+)\(([^)]*)\)")
    let iDisposableTypes = Set.ofList ["FrameView"; "TextView"; "Process"; "FileStream"; "Random"]
    
    let funcMatches = funcPattern.Matches(result)
    for i = funcMatches.Count - 1 downto 0 do
        let m = funcMatches.[i]
        let funcName = m.Groups.[1].Value
        let arg = m.Groups.[2].Value
        
        // newキーワードまたはIDisposableチェック
        let beforeMatch = if m.Index > 3 then result.Substring(m.Index - 4, 4) else ""
        if not (beforeMatch.Contains("new ") || iDisposableTypes.Contains funcName) && not (arg.Contains(",")) then
            result <- result.Substring(0, m.Index) + funcName + " " + arg + result.Substring(m.Index + m.Length)
    
    result

// 改良版ツール（問題ケース対応）
let improvedTool (input: string) : string =
    let mutable result = input
    
    // より安全な順序で処理
    
    // 1. 型キャスト関数の修正（複雑な式は括弧を保持）
    let castPattern = Regex(@"\b(float|int|string|bool|byte|sbyte|int16|uint16|int32|uint32|int64|uint64|decimal|char)\s*\(([^)]+)\)")
    let castMatches = castPattern.Matches(result)
    for i = castMatches.Count - 1 downto 0 do
        let m = castMatches.[i]
        let castType = m.Groups.[1].Value
        let expr = m.Groups.[2].Value
        
        // 複雑な式（演算子含む）は括弧を保持
        if expr.Contains("+") || expr.Contains("-") || expr.Contains("*") || expr.Contains("/") || expr.Contains(".") then
            result <- result.Substring(0, m.Index) + castType + " (" + expr + ")" + result.Substring(m.Index + m.Length)
        else
            result <- result.Substring(0, m.Index) + castType + " " + expr + result.Substring(m.Index + m.Length)
    
    // 2. メソッドチェーンの特別処理（parenthesesが必要な場合）
    let methodChainPattern = Regex(@"(\w+\(\w*\))\.(\w+)")
    let chainMatches = methodChainPattern.Matches(result)
    for i = chainMatches.Count - 1 downto 0 do
        let m = chainMatches.[i]
        let methodCall = m.Groups.[1].Value
        let nextMethod = m.Groups.[2].Value
        
        // 括弧で囲んで優先順位を明確に
        result <- result.Substring(0, m.Index) + "(" + methodCall + ")." + nextMethod + result.Substring(m.Index + m.Length)
    
    // 3. 静的メソッド呼び出しの修正（単一引数のみ）
    let staticMethodPattern = Regex(@"([A-Z]\w*\.[A-Z]\w*)\(([^,)]+)\)")
    result <- staticMethodPattern.Replace(result, "$1 $2")
    
    // 4. インスタンスメソッド呼び出しの修正（空括弧と単一引数）
    let emptyParenPattern = Regex(@"(\w+\.\w+)\(\)")
    result <- emptyParenPattern.Replace(result, "$1")
    
    let singleArgInstancePattern = Regex(@"(\w+\.\w+)\(([^,)]+)\)")
    result <- singleArgInstancePattern.Replace(result, "$1 $2")
    
    result

// テスト実行
printfn "🔍 Problem Cases Analysis:"
printfn "%s" (String.replicate 60 "=")

let mutable currentPassed = 0
let mutable currentFailed = 0
let mutable improvedPassed = 0
let mutable improvedFailed = 0

for (input, expected, description) in problemCases do
    let currentResult = currentToolSimulation input
    let improvedResult = improvedTool input
    
    let currentCorrect = currentResult = expected
    let improvedCorrect = improvedResult = expected
    
    printfn "\n📝 %s" description
    printfn "   Input:    %s" input
    printfn "   Expected: %s" expected
    printfn "   Current:  %s %s" currentResult (if currentCorrect then "✅" else "❌")
    printfn "   Improved: %s %s" improvedResult (if improvedCorrect then "✅" else "❌")
    
    if currentCorrect then currentPassed <- currentPassed + 1 else currentFailed <- currentFailed + 1
    if improvedCorrect then improvedPassed <- improvedPassed + 1 else improvedFailed <- improvedFailed + 1

printfn "\n%s" (String.replicate 60 "=")
printfn "📊 Comparison Summary:"
printfn "   Current Tool:  %d passed, %d failed" currentPassed currentFailed
printfn "   Improved Tool: %d passed, %d failed" improvedPassed improvedFailed

if improvedPassed > currentPassed then
    printfn "\n🎉 Improved tool shows better results! (+%d)" (improvedPassed - currentPassed)
    printfn "💡 これらの改良をメインツールに適用することを推奨します。"
else
    printfn "\n⚠️  さらなる改良が必要です。"

printfn "\n🔧 Recommended improvements for main tool:"
printfn "   1. メソッドチェーンでの括弧保持"
printfn "   2. 型キャストでの複雑式判定"
printfn "   3. 処理順序の最適化"
printfn "   4. 優先順位問題の回避"