#!/usr/bin/env dotnet fsi

open System
open System.Text.RegularExpressions

// 簡易テスト実行
let testCases = [
    // Conservative レベルの成功ケース
    ("float(42)", "float 42", true)
    ("Directory.Exists(path)", "Directory.Exists path", true)
    ("DateTime.Now.ToString(format)", "DateTime.Now.ToString format", true)
    
    // IDisposable保護ケース（変更されないはず）
    ("new FrameView(title)", "new FrameView(title)", false)
    ("new Process()", "new Process()", false)
    
    // 複数引数（変更されないはず）
    ("Path.Combine(dir, file)", "Path.Combine(dir, file)", false)
    ("func(arg1, arg2)", "func(arg1, arg2)", false)
    
    // Standard レベルケース
    ("text.Contains(substr)", "text.Contains substr", true)
    ("calculate(value)", "calculate value", true)
]

// 簡易修正関数
let quickFix (input: string) : string =
    let mutable result = input
    
    // 型キャスト関数
    let castPattern = Regex(@"\b(float|int|string|bool)\s*\(([^,)]+)\)")
    result <- castPattern.Replace(result, "$1 $2")
    
    // 静的メソッド
    let staticPattern = Regex(@"([A-Z]\w*\.[A-Z]\w*)\(([^,)]+)\)")
    result <- staticPattern.Replace(result, "$1 $2")
    
    // インスタンスメソッド
    let instancePattern = Regex(@"(\w+\.\w+)\(([^,)]+)\)")
    result <- instancePattern.Replace(result, "$1 $2")
    
    // 関数呼び出し（IDisposableチェック）
    let funcPattern = Regex(@"\b(\w+)\(([^,)]+)\)")
    let iDisposableTypes = Set.ofList ["FrameView"; "TextView"; "Process"; "FileStream"]
    
    let funcMatches = funcPattern.Matches(result)
    for i = funcMatches.Count - 1 downto 0 do
        let m = funcMatches.[i]
        let funcName = m.Groups.[1].Value
        let arg = m.Groups.[2].Value
        
        // newキーワードまたはIDisposableチェック
        let beforeMatch = if m.Index > 3 then result.Substring(m.Index - 4, 4) else ""
        if not (beforeMatch.Contains("new ") || iDisposableTypes.Contains funcName) then
            result <- result.Substring(0, m.Index) + funcName + " " + arg + result.Substring(m.Index + m.Length)
    
    result

// テスト実行
printfn "🧪 Quick Test Results:"
printfn "%s" (String.replicate 50 "=")

let mutable passed = 0
let mutable failed = 0

for (input, expected, shouldChange) in testCases do
    let actual = quickFix input
    let changed = actual <> input
    let testPassed = 
        if shouldChange then actual = expected && changed
        else actual = expected && not changed
    
    let status = if testPassed then "✅ PASS" else "❌ FAIL"
    printfn "%s %s" status input
    
    if not testPassed then
        printfn "    Expected: %s" expected
        printfn "    Actual:   %s" actual
        printfn "    Changed:  %b (should be %b)" changed shouldChange
        failed <- failed + 1
    else
        passed <- passed + 1

printfn ""
printfn "📊 Summary: %d passed, %d failed" passed failed

if failed > 0 then
    printfn "\n💡 失敗したテストケースがあります。メインツールの改良が必要です。"
else
    printfn "\n✅ 全テストが成功しました！"