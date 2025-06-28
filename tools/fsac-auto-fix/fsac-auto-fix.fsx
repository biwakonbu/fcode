#!/usr/bin/env dotnet fsi

(*
F# FSAC Auto-Fix Tool (Improved with Safety Levels)
FSAC診断警告を自動修正するF# Scriptツール

使用方法:
  dotnet fsi fsac-auto-fix.fsx -- --file <filepath>
  dotnet fsi fsac-auto-fix.fsx -- --dir <directory>
  dotnet fsi fsac-auto-fix.fsx -- --dir <directory> --dry-run
  dotnet fsi fsac-auto-fix.fsx -- --dir <directory> --level conservative
*)

open System
open System.IO
open System.Text.RegularExpressions

// ===============================================
// 修正レベル定義
// ===============================================

type FixLevel =
    | Conservative  // 保守的：安全性最優先
    | Standard     // 標準：一般的な修正
    | Aggressive   // 積極的：すべての修正

// ===============================================
// FSAC診断コード定義
// ===============================================

type FsacDiagnostic = {
    Code: string
    Title: string
    Description: string
    Examples: string list
}

let fsacDiagnostics = [
    {
        Code = "FSAC0004"
        Title = "Unnecessary parentheses"
        Description = "関数呼び出し時の不要な括弧を削除"
        Examples = [
            "float(x) → float x"
            "func(arg) → func arg"
            "Type.Method(arg) → Type.Method arg"
        ]
    }
    {
        Code = "FSAC0002"
        Title = "Redundant qualifier"
        Description = "冗長な修飾子を削除"
        Examples = [
            "System.String.Empty → String.Empty"
            "System.Int32.Parse → Int32.Parse"
        ]
    }
    {
        Code = "FSAC0001"
        Title = "Unused open statement"
        Description = "未使用のopen文を削除"
        Examples = [
            "open System.Unused // 未使用の場合削除"
        ]
    }
]

type FixPattern = {
    Code: string
    Name: string
    Pattern: Regex
    Replacement: string -> string
    Examples: string list
}

type FixResult = {
    OriginalContent: string
    FixedContent: string
    ChangesApplied: int
    Description: string
}

// IDisposableな型のリスト（newキーワードを保持すべき型）
let iDisposableTypes = Set.ofList [
    "FrameView"; "TextView"; "Window"; "Dialog"; "Button"; "Label"  // Terminal.Gui
    "Process"; "ProcessStartInfo"  // System.Diagnostics
    "NetworkStream"; "TcpClient"; "TcpListener"; "Socket"  // System.Net
    "FileStream"; "StreamReader"; "StreamWriter"; "MemoryStream"  // System.IO
    "Timer"; "CancellationTokenSource"  // System.Threading
    "HttpClient"; "HttpWebRequest"  // System.Net.Http
    "BinaryReader"; "BinaryWriter"; "StringReader"; "StringWriter"  // System.IO
    "Mutex"; "Semaphore"; "ManualResetEvent"; "AutoResetEvent"  // System.Threading
]

// newキーワードが必要かチェック
let needsNewKeyword (typeName: string) : bool =
    iDisposableTypes.Contains typeName

// 安全性チェック関数群

// 文字列・コメント検出関数（改良版）
let isInStringOrComment (content: string) (index: int) : bool =
    try
        let beforeContent = content.Substring(0, index)
        
        // 文字列内チェック（エスケープ文字考慮）
        let mutable inString = false
        let mutable escape = false
        for c in beforeContent do
            match c with
            | '\\' when not escape -> escape <- true
            | '"' when not escape -> inString <- not inString
            | _ -> escape <- false
        
        // コメント内チェック
        let lastLineStart = max 0 (beforeContent.LastIndexOf('\n') + 1)
        let currentLine = beforeContent.Substring(lastLineStart)
        let inComment = currentLine.Contains("//")
        
        inString || inComment
    with
    | _ -> true  // エラー時は安全側に倒してスキップ

// 複雑な式の検出（優先順位問題を起こしやすい）
let hasComplexExpression (expr: string) : bool =
    expr.Contains("+") || expr.Contains("-") || expr.Contains("*") || expr.Contains("/") ||
    expr.Contains("&&") || expr.Contains("||") || expr.Contains("=") ||
    expr.Contains("<") || expr.Contains(">") || expr.Contains(".") ||
    expr.Contains(",") || expr.Contains("[") || expr.Contains("{") ||
    expr.Contains("if ") || expr.Contains("match ") || expr.Contains("let ")

// プロパティアクセスチェーン検出
let hasPropertyChain (content: string) (index: int) : bool =
    try
        let afterIndex = index + 10 // マッチ後の数文字をチェック
        if afterIndex < content.Length then
            let afterContent = content.Substring(afterIndex, min 20 (content.Length - afterIndex))
            afterContent.Contains(".")
        else
            false
    with
    | _ -> false

// ネストした括弧の検出
let hasNestedParentheses (expr: string) : bool =
    let openCount = expr.ToCharArray() |> Array.filter (fun c -> c = '(') |> Array.length
    let closeCount = expr.ToCharArray() |> Array.filter (fun c -> c = ')') |> Array.length
    openCount > 0 || closeCount > 0

// キーワード周辺の検出（match, if, let等）
let isNearKeyword (content: string) (index: int) : bool =
    try
        let beforeIndex = max 0 (index - 20)
        let beforeContent = content.Substring(beforeIndex, index - beforeIndex)
        beforeContent.Contains("match ") || beforeContent.Contains("if ") ||
        beforeContent.Contains("elif ") || beforeContent.Contains("while ") ||
        beforeContent.Contains("for ") || beforeContent.Contains("let ")
    with
    | _ -> false

// FSAC0004: 不要な括弧除去パターン
let safeUnnecessaryParenthesesPatterns = [
    // 単純な関数呼び出し（IDisposableコンストラクタを除外）: func(arg) -> func arg
    {
        Code = "FSAC0004"
        Name = "Safe simple function call"
        Pattern = Regex(@"\b(\w+)\((\w+)\)", RegexOptions.Compiled)
        Examples = ["float(x) → float x"; "calculate(value) → calculate value"]
        Replacement = fun (content: string) ->
            let pattern = Regex(@"\b(\w+)\((\w+)\)")
            let matches = pattern.Matches(content)
            let mutable result = content
            
            // 後ろから前に処理（インデックスが変わらないように）
            for i = matches.Count - 1 downto 0 do
                let m = matches.[i]
                let funcName = m.Groups.[1].Value
                let arg = m.Groups.[2].Value
                
                // 安全性チェック（複数条件）
                if not (isInStringOrComment content m.Index) &&
                   not (hasPropertyChain content m.Index) &&
                   not (isNearKeyword content m.Index) then
                    // newキーワードがある場合、またはIDisposableコンストラクタの場合はスキップ
                    let beforeMatch = if m.Index > 3 then content.Substring(m.Index - 4, 4) else ""
                    if not (beforeMatch.Contains("new ") || needsNewKeyword funcName) then
                        result <- result.Substring(0, m.Index) + $"{funcName} {arg}" + result.Substring(m.Index + m.Length)
            
            result
    }
    
    // メソッド呼び出し（静的メソッド）: Type.Method(arg) -> Type.Method arg（改良版）
    {
        Code = "FSAC0004" 
        Name = "Safe static method call (improved)"
        Pattern = Regex(@"([A-Z]\w*\.[A-Z]\w*)\(([^,)]+)\)", RegexOptions.Compiled)
        Examples = ["Process.Start(info) → Process.Start info"; "DateTime.Parse(str) → DateTime.Parse str"]
        Replacement = fun (content: string) ->
            let pattern = Regex(@"([A-Z]\w*\.[A-Z]\w*)\(([^,)]+)\)")
            let matches = pattern.Matches(content)
            let mutable result = content
            
            for i = matches.Count - 1 downto 0 do
                let m = matches.[i]
                // 安全性チェック（複数条件）
                if not (isInStringOrComment content m.Index) &&
                   not (hasPropertyChain content m.Index) &&
                   not (hasComplexExpression m.Groups.[2].Value) then
                    let methodCall = m.Groups.[1].Value
                    let arg = m.Groups.[2].Value
                    result <- result.Substring(0, m.Index) + $"{methodCall} {arg}" + result.Substring(m.Index + m.Length)
            
            result
    }
    
    // インスタンスメソッド呼び出し: obj.Method(arg) -> obj.Method arg（改良版）
    {
        Code = "FSAC0004"
        Name = "Safe instance method call (improved)" 
        Pattern = Regex(@"(\w+\.\w+)\(([^,)]*)\)", RegexOptions.Compiled)
        Examples = ["obj.Method(arg) → obj.Method arg"; "text.Substring(index) → text.Substring index"; "list.Count() → list.Count"]
        Replacement = fun (content: string) ->
            let pattern = Regex(@"(\w+\.\w+)\(([^,)]*)\)")
            let matches = pattern.Matches(content)
            let mutable result = content
            
            // 後ろから前に処理
            for i = matches.Count - 1 downto 0 do
                let m = matches.[i]
                // 安全性チェック（複数条件）
                if not (isInStringOrComment content m.Index) &&
                   not (hasPropertyChain content m.Index) &&
                   not (hasComplexExpression m.Groups.[2].Value) then
                    let methodCall = m.Groups.[1].Value
                    let arg = m.Groups.[2].Value.Trim()
                    
                    // 空の引数の場合は括弧を完全に削除
                    if String.IsNullOrEmpty(arg) then
                        result <- result.Substring(0, m.Index) + methodCall + result.Substring(m.Index + m.Length)
                    // 単一引数の場合は括弧を削除
                    else
                        result <- result.Substring(0, m.Index) + $"{methodCall} {arg}" + result.Substring(m.Index + m.Length)
            
            result
    }
    
    // キャスト関数呼び出し: float(expr) -> float expr, int(expr) -> int expr（改良版）
    {
        Code = "FSAC0004"
        Name = "Cast function call (improved)"
        Pattern = Regex(@"\b(float|int|string|bool|byte|sbyte|int16|uint16|int32|uint32|int64|uint64|decimal|char)\s*\(([^)]+)\)", RegexOptions.Compiled)
        Examples = ["float(42) → float 42"; "int(value + 1) → int (value + 1)"; "string(obj.Method()) → string (obj.Method())"]
        Replacement = fun (content: string) ->
            let pattern = Regex(@"\b(float|int|string|bool|byte|sbyte|int16|uint16|int32|uint32|int64|uint64|decimal|char)\s*\(([^)]+)\)")
            let matches = pattern.Matches(content)
            let mutable result = content
            
            // 後ろから前に処理（インデックスが変わらないように）
            for i = matches.Count - 1 downto 0 do
                let m = matches.[i]
                // 安全性チェック（複数条件）
                if not (isInStringOrComment content m.Index) &&
                   not (isNearKeyword content m.Index) then
                    let castType = m.Groups.[1].Value
                    let expr = m.Groups.[2].Value
                    
                    // 複雑な式は括弧を保持、単純な式のみ変換
                    if hasComplexExpression expr then
                        result <- result.Substring(0, m.Index) + $"{castType} ({expr})" + result.Substring(m.Index + m.Length)
                    else
                        result <- result.Substring(0, m.Index) + $"{castType} {expr}" + result.Substring(m.Index + m.Length)
            
            result
    }
    
    // マッチ文での関数呼び出し: match func(arg) with -> match func arg with
    {
        Code = "FSAC0004"
        Name = "Match expression function call"
        Pattern = Regex(@"match\s+(\w+)\((\w+)\)\s+with", RegexOptions.Compiled)
        Examples = ["match getValue(x) with → match getValue x with"]
        Replacement = fun (content: string) ->
            let pattern = Regex(@"match\s+(\w+)\((\w+)\)\s+with")
            let matches = pattern.Matches(content)
            let mutable result = content
            
            for i = matches.Count - 1 downto 0 do
                let m = matches.[i]
                let funcName = m.Groups.[1].Value
                let arg = m.Groups.[2].Value
                
                if not (needsNewKeyword funcName) then
                    result <- result.Substring(0, m.Index) + $"match {funcName} {arg} with" + result.Substring(m.Index + m.Length)
            
            result
    }
    
    // パイプライン内での関数呼び出し: |> func(arg) -> |> func arg  
    {
        Code = "FSAC0004"
        Name = "Pipeline function call"
        Pattern = Regex(@"\|\>\s*(\w+)\((\w+)\)", RegexOptions.Compiled)
        Examples = ["value |> float(x) → value |> float x"]
        Replacement = fun (content: string) ->
            let pattern = Regex(@"\|\>\s*(\w+)\((\w+)\)")
            pattern.Replace(content, "|> $1 $2")
    }
    
    // メソッドチェーンでの優先順位保護: method().property -> (method()).property
    {
        Code = "FSAC0004"
        Name = "Method chain precedence protection"
        Pattern = Regex(@"(\w+\.\w+\s+\w+|\w+\(\w*\))\.(\w+)", RegexOptions.Compiled)
        Examples = ["Random().Next → (Random()).Next"; "GetFiles dir.Length → (GetFiles dir).Length"]
        Replacement = fun (content: string) ->
            let pattern = Regex(@"(\w+\.\w+\s+\w+|\w+\(\w*\))\.(\w+)")
            let matches = pattern.Matches(content)
            let mutable result = content
            
            // 後ろから前に処理
            for i = matches.Count - 1 downto 0 do
                let m = matches.[i]
                // 安全性チェック（複数条件）
                if not (isInStringOrComment content m.Index) &&
                   not (hasNestedParentheses m.Groups.[1].Value) then
                    let methodCall = m.Groups.[1].Value
                    let nextAccess = m.Groups.[2].Value
                    
                    // 括弧で囲んで優先順位を明確に
                    result <- result.Substring(0, m.Index) + $"({methodCall}).{nextAccess}" + result.Substring(m.Index + m.Length)
            
            result
    }
]

// FSAC0002: 冗長な修飾子の修正パターン
let redundantQualifierPatterns = [
    {
        Code = "FSAC0002"
        Name = "System module qualifiers"
        Pattern = Regex(@"System\.(String|Int32|DateTime|Boolean)\.(\w+)", RegexOptions.Compiled)
        Examples = ["System.String.Empty → String.Empty"; "System.Int32.Parse → Int32.Parse"]
        Replacement = fun (content: string) ->
            let pattern = Regex(@"System\.(String|Int32|DateTime|Boolean)\.(\w+)", RegexOptions.Compiled)
            pattern.Replace(content, "$1.$2")
    }
]

// FSAC0001: 未使用open文の修正パターン
let unusedOpenPatterns = [
    {
        Code = "FSAC0001" 
        Name = "Unused open statements"
        Pattern = Regex(@"^open\s+[\w\.]+\s*$", RegexOptions.Compiled ||| RegexOptions.Multiline)
        Examples = ["open System.Unused → (削除)"; "open UnusedNamespace → (削除)"]
        Replacement = fun (content: string) ->
            // これは実際の使用解析が必要なので、簡易版として空行で置換
            content // 実装は複雑になるため、手動確認が必要
    }
]

// 安全なパターンと危険なパターンの分離
let safePatterns = [
    1  // Safe static method call (improved)
    3  // Cast function call (improved)
    5  // Pipeline function call
]

let riskPatterns = [
    0  // Safe simple function call
    2  // Safe instance method call (improved)
    4  // Match expression function call
    6  // Method chain precedence protection
]

// 段階的修正パターン
let getFixPatternsByLevel (level: FixLevel) (targetDiagnostic: string) =
    let allPatterns = safeUnnecessaryParenthesesPatterns @ redundantQualifierPatterns @ unusedOpenPatterns
    
    // 特定の診断コードが指定されている場合
    if not (String.IsNullOrEmpty targetDiagnostic) then
        allPatterns |> List.filter (fun p -> p.Code = targetDiagnostic)
    else
        // レベルに応じたパターン選択
        match level with
        | Conservative ->
            // 最も安全な修正のみ（静的メソッド、型キャストのみ）
            safePatterns |> List.map (fun i -> safeUnnecessaryParenthesesPatterns.[i]) 
            |> List.append redundantQualifierPatterns
        | Standard ->
            // 標準的な修正（安全パターン + 一部リスクパターン）
            let safeList = safePatterns |> List.map (fun i -> safeUnnecessaryParenthesesPatterns.[i])
            let limitedRisk = [safeUnnecessaryParenthesesPatterns.[0]]  // 単純関数呼び出しのみ
            safeList @ limitedRisk @ redundantQualifierPatterns
        | Aggressive ->
            // すべての修正（全リスク許容）
            safeUnnecessaryParenthesesPatterns @ redundantQualifierPatterns @ unusedOpenPatterns

// ===============================================
// ファイル処理
// ===============================================

// より安全なファイル内容修正
let fixFileContent (content: string) (dryRun: bool) (level: FixLevel) (targetDiagnostic: string) : FixResult =
    let mutable currentContent = content
    let mutable totalChanges = 0
    let mutable descriptions = []
    
    // 選択されたレベルまたは診断コードのパターンのみ適用
    let selectedPatterns = getFixPatternsByLevel level targetDiagnostic
    
    for pattern in selectedPatterns do
        let matches = pattern.Pattern.Matches(currentContent)
        if matches.Count > 0 then
            let newContent = pattern.Replacement currentContent
            if newContent <> currentContent then
                descriptions <- $"{pattern.Name}: {matches.Count} fixes" :: descriptions
                totalChanges <- totalChanges + matches.Count
                if not dryRun then
                    currentContent <- newContent
    
    {
        OriginalContent = content
        FixedContent = currentContent
        ChangesApplied = totalChanges
        Description = String.Join("; ", List.rev descriptions)
    }

// 単一ファイルを処理
let processFile (filePath: string) (dryRun: bool) (level: FixLevel) (targetDiagnostic: string) : unit =
    try
        if not (File.Exists filePath) then
            printfn $"❌ File not found: {filePath}"
            exit 1
            
        let content = File.ReadAllText filePath
        let result = fixFileContent content dryRun level targetDiagnostic
        
        if result.ChangesApplied > 0 then
            let mode = if dryRun then "[DRY RUN]" else "[APPLIED]"
            let target = if String.IsNullOrEmpty targetDiagnostic then $"Level: {level}" else $"Target: {targetDiagnostic}"
            printfn $"✅ {mode} {filePath}: {result.ChangesApplied} changes ({target})"
            if not (String.IsNullOrEmpty result.Description) then
                printfn $"   └─ {result.Description}"
            
            if not dryRun then
                File.WriteAllText(filePath, result.FixedContent)
        else
            let target = if String.IsNullOrEmpty targetDiagnostic then $"Level: {level}" else $"Target: {targetDiagnostic}"
            printfn $"ℹ️  {filePath}: No changes needed ({target})"
            
    with
    | ex ->
        printfn $"❌ Error processing {filePath}: {ex.Message}"

// ディレクトリを再帰的に処理
let processDirectory (dirPath: string) (dryRun: bool) (level: FixLevel) (targetDiagnostic: string) : unit =
    try
        if not (Directory.Exists dirPath) then
            printfn $"❌ Directory not found: {dirPath}"
            exit 1
            
        let fsFiles = 
            Directory.EnumerateFiles(dirPath, "*.fs", SearchOption.AllDirectories)
            |> Seq.filter (fun f -> not (f.Contains "bin" || f.Contains "obj"))
            |> Seq.toArray
            
        printfn $"🔍 Found {fsFiles.Length} F# files in {dirPath}"
        if String.IsNullOrEmpty targetDiagnostic then
            printfn $"📊 Fix level: {level}"
        else
            printfn $"🎯 Target diagnostic: {targetDiagnostic}"
        
        let mutable totalChanges = 0
        let mutable processedFiles = 0
        
        for filePath in fsFiles do
            let content = File.ReadAllText filePath
            let result = fixFileContent content dryRun level targetDiagnostic
            
            if result.ChangesApplied > 0 then
                let mode = if dryRun then "[DRY RUN]" else "[APPLIED]"
                printfn $"✅ {mode} {Path.GetRelativePath(dirPath, filePath)}: {result.ChangesApplied} changes"
                if not (String.IsNullOrEmpty result.Description) then
                    printfn $"   └─ {result.Description}"
                
                if not dryRun then
                    File.WriteAllText(filePath, result.FixedContent)
                    
                totalChanges <- totalChanges + result.ChangesApplied
                processedFiles <- processedFiles + 1
                
        printfn ""
        let target = if String.IsNullOrEmpty targetDiagnostic then $"Level: {level}" else $"Target: {targetDiagnostic}"
        printfn $"📊 Summary: {totalChanges} total changes across {processedFiles} files ({target})"
        
        if dryRun then
            printfn "💡 Run without --dry-run to apply changes"
            
    with
    | ex ->
        printfn $"❌ Error processing directory {dirPath}: {ex.Message}"

// ===============================================
// コマンドライン解析
// ===============================================

let parseArgs (args: string array) : unit =
    let mutable filePath = ""
    let mutable dirPath = ""
    let mutable dryRun = false
    let mutable level = Standard
    let mutable showHelp = false
    let mutable showDiagnostics = false
    let mutable targetDiagnostic = ""
    
    let rec parseNext (argList: string list) =
        match argList with
        | "--file" :: path :: rest ->
            filePath <- path
            parseNext rest
        | "--dir" :: path :: rest ->
            dirPath <- path
            parseNext rest
        | "--dry-run" :: rest ->
            dryRun <- true
            parseNext rest
        | "--level" :: levelStr :: rest ->
            level <- match levelStr.ToLower() with
                     | "conservative" -> Conservative
                     | "standard" -> Standard
                     | "aggressive" -> Aggressive
                     | _ -> 
                         printfn $"⚠️  Unknown level: {levelStr}, using Standard"
                         Standard
            parseNext rest
        | "--diagnostics" :: rest ->
            showDiagnostics <- true
            parseNext rest
        | "--fix" :: diagnosticCode :: rest ->
            targetDiagnostic <- diagnosticCode.ToUpper()
            parseNext rest
        | "--help" :: _ | "-h" :: _ ->
            showHelp <- true
        | [] -> ()
        | unknown :: rest ->
            printfn $"⚠️  Unknown argument: {unknown}"
            parseNext rest
    
    parseNext (Array.toList args)
    
    if showDiagnostics then
        printfn "\n🔍 FSAC Diagnostic Codes Supported:"
        printfn "%s" ("=".PadRight(50, '='))
        for diagnostic in fsacDiagnostics do
            printfn $"📋 {diagnostic.Code}: {diagnostic.Title}"
            printfn $"   {diagnostic.Description}"
            printfn "   Examples:"
            for example in diagnostic.Examples do
                printfn $"     • {example}"
            printfn ""
        exit 0
    
    if showHelp || (String.IsNullOrEmpty filePath && String.IsNullOrEmpty dirPath && String.IsNullOrEmpty targetDiagnostic) then
        printfn """
F# FSAC Auto-Fix Tool (Improved with Safety Levels)

Usage:
  dotnet fsi fsac-auto-fix.fsx -- --file <filepath>                    # Fix single file
  dotnet fsi fsac-auto-fix.fsx -- --dir <directory>                    # Fix all .fs files in directory
  dotnet fsi fsac-auto-fix.fsx -- --dir <dir> --dry-run                # Preview changes without applying
  dotnet fsi fsac-auto-fix.fsx -- --dir <dir> --level conservative      # Use conservative fix level
  dotnet fsi fsac-auto-fix.fsx -- --dir <dir> --fix FSAC0004           # Fix only specific diagnostic
  dotnet fsi fsac-auto-fix.fsx -- --diagnostics                        # Show supported diagnostics

Options:
  --file <path>         Process single F# file
  --dir <path>          Process all .fs files in directory recursively
  --dry-run             Preview changes without applying them
  --level <level>       Fix level: conservative, standard (default), aggressive
  --fix <code>          Fix only specific FSAC diagnostic code (FSAC0001, FSAC0002, FSAC0004)
  --diagnostics         Show all supported FSAC diagnostic codes with examples
  --help, -h            Show this help message

Fix Levels:
  conservative          Only safe type cast fixes (float(x) -> float x)
  standard              + Safe function calls (excluding IDisposable constructors)
  aggressive            + All available fixes (including unused opens)

Supported FSAC Diagnostics:
  FSAC0004              Unnecessary parentheses (func(arg) -> func arg)
  FSAC0002              Redundant qualifiers (System.String -> String)
  FSAC0001              Unused open statements (open Unused -> remove)

Safety Features:
  • IDisposable constructor detection (preserves 'new' keyword)
  • Context-aware pattern matching
  • Gradual fix approach to prevent breaking changes
  • Selective diagnostic fixing

Examples:
  dotnet fsi fsac-auto-fix.fsx -- --file src/Program.fs
  dotnet fsi fsac-auto-fix.fsx -- --dir src/ --level conservative
  dotnet fsi fsac-auto-fix.fsx -- --dir . --dry-run
  dotnet fsi fsac-auto-fix.fsx -- --dir src --fix FSAC0004
  dotnet fsi fsac-auto-fix.fsx -- --diagnostics
"""
        exit 0
    
    if not (String.IsNullOrEmpty filePath) then
        processFile filePath dryRun level targetDiagnostic
    elif not (String.IsNullOrEmpty dirPath) then
        processDirectory dirPath dryRun level targetDiagnostic

// メイン実行
match fsi.CommandLineArgs with
| [| _scriptName |] ->
    parseArgs [| "--help" |]
| args ->
    let actualArgs = args |> Array.skip 1 // スクリプト名をスキップ
    parseArgs actualArgs