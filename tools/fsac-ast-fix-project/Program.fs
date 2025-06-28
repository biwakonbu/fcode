module FCode.Tools.AstFix

open System
open System.IO
open FSharp.Compiler.CodeAnalysis
open FSharp.Compiler.Syntax
open FSharp.Compiler.Text
open Fantomas.Core

// ===============================================
// 修正レベル定義
// ===============================================

type FixLevel =
    | Conservative  // 保守的：型キャストのみ
    | Standard     // 標準：安全な関数呼び出し修正
    | Aggressive   // 積極的：すべての修正

// ===============================================
// 安全な変換判定
// ===============================================

// IDisposableな型のリスト
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

// 基本型キャスト関数のリスト
let castFunctions = Set.ofList [
    "float"; "int"; "string"; "bool"; "byte"; "sbyte"
    "int16"; "uint16"; "int32"; "uint32"; "int64"; "uint64"
    "decimal"; "char"
]

// 型名がIDisposableかチェック
let isDisposableType (typeName: string) : bool =
    iDisposableTypes.Contains typeName

// ===============================================
// AST解析・変換
// ===============================================

type AnalysisResult = {
    FilePath: string
    TotalExpressions: int
    UnnecessaryParentheses: int
    SafeTransformations: int
    UnsafeTransformations: int
    Recommendations: string list
}

// 式を分析してより安全な変換候補を特定
let rec analyzeExpression (expr: SynExpr) : (string * bool) list =
    match expr with
    // 関数呼び出し: func(arg)
    | SynExpr.App (_, false, SynExpr.Ident ident, SynExpr.Paren (argExpr, _, _, _), _) ->
        let isSafe = castFunctions.Contains ident.idText || not (isDisposableType ident.idText)
        let recommendation = $"Remove parentheses from '{ident.idText}(...)'"
        [(recommendation, isSafe)] @ analyzeExpression argExpr
    
    // メソッド呼び出し: obj.Method(arg)
    | SynExpr.App (_, false, SynExpr.DotGet (objExpr, _, longDotId, _), SynExpr.Paren (argExpr, _, _, _), _) ->
        let methodName = longDotId.ToString()
        let recommendation = $"Remove parentheses from method call '{methodName}(...)'"
        [(recommendation, true)] @ analyzeExpression objExpr @ analyzeExpression argExpr
    
    // 括弧式: (expr)
    | SynExpr.Paren (innerExpr, _, _, _) ->
        analyzeExpression innerExpr
    
    // Let束縛
    | SynExpr.LetOrUse (_, _, bindings, body, _, _) ->
        let bindingAnalysis = bindings |> List.collect (fun binding ->
            match binding with
            | SynBinding (_, _, _, _, _, _, _, _, _, expr, _, _, _) -> analyzeExpression expr)
        bindingAnalysis @ analyzeExpression body
    
    // マッチ式
    | SynExpr.Match (_, expr, clauses, _, _) ->
        let exprAnalysis = analyzeExpression expr
        let clauseAnalysis = clauses |> List.collect (fun clause ->
            match clause with
            | SynMatchClause (_, _, whenExpr, resultExpr, _, _) ->
                let whenAnalysis = match whenExpr with Some e -> analyzeExpression e | None -> []
                whenAnalysis @ analyzeExpression resultExpr)
        exprAnalysis @ clauseAnalysis
    
    // その他の式（再帰的に子要素を解析）
    | SynExpr.App (_, _, funcExpr, argExpr, _) ->
        analyzeExpression funcExpr @ analyzeExpression argExpr
    
    | _ -> []

// モジュール宣言を分析
let analyzeModuleDecl (decl: SynModuleDecl) : (string * bool) list =
    match decl with
    | SynModuleDecl.Let (_, bindings, _) ->
        bindings |> List.collect (fun binding ->
            match binding with
            | SynBinding (_, _, _, _, _, _, _, _, _, expr, _, _, _) -> analyzeExpression expr)
    | _ -> []

// ファイル全体を分析
let analyzeFile (filePath: string) (level: FixLevel) : AnalysisResult =
    try
        let source = File.ReadAllText filePath
        let sourceText = SourceText.ofString source
        
        // F#コンパイラサービスでパース
        let checker = FSharpChecker.Create()
        let parsingOptions = { FSharpParsingOptions.Default with SourceFiles = [| filePath |] }
        
        let parseResults = 
            checker.ParseFile(filePath, sourceText, parsingOptions)
            |> Async.RunSynchronously
        
        match parseResults.ParseTree with
        | Some (ParsedInput.ImplFile (ParsedImplFileInput (_, _, _, _, _, modules, _, _, _))) ->
            let allRecommendations = 
                modules |> List.collect (fun moduleOrNamespace ->
                    match moduleOrNamespace with
                    | SynModuleOrNamespace (_, _, _, decls, _, _, _, _, _) ->
                        decls |> List.collect analyzeModuleDecl)
            
            let safeTransformations = allRecommendations |> List.filter snd |> List.length
            let unsafeTransformations = allRecommendations |> List.filter (not << snd) |> List.length
            let recommendations = allRecommendations |> List.map fst
            
            {
                FilePath = filePath
                TotalExpressions = allRecommendations.Length
                UnnecessaryParentheses = allRecommendations.Length
                SafeTransformations = safeTransformations
                UnsafeTransformations = unsafeTransformations
                Recommendations = recommendations
            }
        
        | _ ->
            {
                FilePath = filePath
                TotalExpressions = 0
                UnnecessaryParentheses = 0
                SafeTransformations = 0
                UnsafeTransformations = 0
                Recommendations = ["Failed to parse file"]
            }
            
    with
    | ex ->
        {
            FilePath = filePath
            TotalExpressions = 0
            UnnecessaryParentheses = 0
            SafeTransformations = 0
            UnsafeTransformations = 0
            Recommendations = [$"Error: {ex.Message}"]
        }

// ===============================================
// レポート生成
// ===============================================

let generateReport (results: AnalysisResult list) (level: FixLevel) : unit =
    printfn ""
    printfn "🔍 F# AST Analysis Report"
    printfn $"📊 Fix Level: {level}"
    printfn $"📁 Files Analyzed: {results.Length}"
    printfn ""
    
    let totalSafe = results |> List.sumBy (_.SafeTransformations)
    let totalUnsafe = results |> List.sumBy (_.UnsafeTransformations)
    let totalRecommendations = totalSafe + totalUnsafe
    
    printfn $"✅ Safe Transformations Available: {totalSafe}"
    printfn $"⚠️  Potentially Unsafe Transformations: {totalUnsafe}"
    printfn $"📈 Total Recommendations: {totalRecommendations}"
    printfn ""
    
    // ファイル別詳細
    for result in results do
        if result.SafeTransformations > 0 || result.UnsafeTransformations > 0 then
            printfn $"📄 {Path.GetFileName result.FilePath}:"
            printfn $"   ✅ Safe: {result.SafeTransformations}, ⚠️  Unsafe: {result.UnsafeTransformations}"
            
            // レベルに応じて推奨事項を表示
            let relevantRecommendations = 
                match level with
                | Conservative -> result.Recommendations |> List.take (min 3 result.Recommendations.Length)
                | Standard -> result.Recommendations |> List.take (min 5 result.Recommendations.Length)
                | Aggressive -> result.Recommendations
            
            for recommendation in relevantRecommendations |> List.take (min 3 relevantRecommendations.Length) do
                printfn $"      • {recommendation}"
            
            if relevantRecommendations.Length > 3 then
                printfn $"      ... and {relevantRecommendations.Length - 3} more"
            printfn ""
    
    printfn ""
    printfn "💡 Recommendations:"
    match level with
    | Conservative ->
        printfn "   • Start with Conservative level to apply only the safest transformations"
        printfn "   • Focus on type cast functions: float(x) -> float x"
    | Standard ->
        printfn "   • Current level applies most safe transformations"
        printfn "   • Review any remaining unsafe transformations manually"
    | Aggressive ->
        printfn "   • All available transformations will be applied"
        printfn "   • Carefully review changes before committing"
    
    printfn ""
    printfn "🔧 To apply transformations:"
    printfn "   • Use the original fsac-auto-fix.fsx for actual modifications"
    printfn "   • This AST analyzer provides safe transformation guidance"

// ===============================================
// メイン処理
// ===============================================

let processDirectory (dirPath: string) (level: FixLevel) : unit =
    try
        if not (Directory.Exists dirPath) then
            printfn $"❌ Directory not found: {dirPath}"
            exit 1
            
        let fsFiles = 
            Directory.EnumerateFiles(dirPath, "*.fs", SearchOption.AllDirectories)
            |> Seq.filter (fun f -> not (f.Contains "bin" || f.Contains "obj"))
            |> Seq.toArray
            
        printfn $"🔍 Analyzing {fsFiles.Length} F# files in {dirPath}..."
        
        let results = 
            fsFiles 
            |> Array.map (fun filePath -> analyzeFile filePath level)
            |> Array.toList
        
        generateReport results level
            
    with
    | ex ->
        printfn $"❌ Error processing directory {dirPath}: {ex.Message}"

let processFile (filePath: string) (level: FixLevel) : unit =
    try
        if not (File.Exists filePath) then
            printfn $"❌ File not found: {filePath}"
            exit 1
            
        let result = analyzeFile filePath level
        generateReport [result] level
            
    with
    | ex ->
        printfn $"❌ Error processing file {filePath}: {ex.Message}"

// ===============================================
// コマンドライン解析
// ===============================================

[<EntryPoint>]
let main args =
    let mutable filePath = ""
    let mutable dirPath = ""
    let mutable level = Standard
    let mutable showHelp = false
    
    let rec parseArgs (argList: string list) =
        match argList with
        | "--file" :: path :: rest ->
            filePath <- path
            parseArgs rest
        | "--dir" :: path :: rest ->
            dirPath <- path
            parseArgs rest
        | "--level" :: levelStr :: rest ->
            level <- match levelStr.ToLower() with
                     | "conservative" -> Conservative
                     | "standard" -> Standard
                     | "aggressive" -> Aggressive
                     | _ -> 
                         printfn $"⚠️  Unknown level: {levelStr}, using Standard"
                         Standard
            parseArgs rest
        | "--help" :: _ | "-h" :: _ ->
            showHelp <- true
            parseArgs []
        | [] -> ()
        | unknown :: rest ->
            printfn $"⚠️  Unknown argument: {unknown}"
            parseArgs rest
    
    parseArgs (Array.toList args)
    
    if showHelp || (String.IsNullOrEmpty filePath && String.IsNullOrEmpty dirPath) then
        printfn """
F# AST Analysis Tool for Safe Code Transformations

Usage:
  dotnet run -- --file <filepath>                    # Analyze single file
  dotnet run -- --dir <directory>                    # Analyze all .fs files in directory
  dotnet run -- --dir <dir> --level conservative     # Use conservative analysis level

Options:
  --file <path>         Analyze single F# file
  --dir <path>          Analyze all .fs files in directory recursively
  --level <level>       Analysis level: conservative, standard (default), aggressive
  --help, -h            Show this help message

Analysis Levels:
  conservative          Focus on safest transformations (type casts)
  standard              Include safe function calls (excluding IDisposable constructors)
  aggressive            All potential transformations

Examples:
  dotnet run -- --file src/Program.fs
  dotnet run -- --dir src/ --level conservative
  dotnet run -- --dir .

This tool provides AST-based analysis to guide safe code transformations.
Use the recommendations to inform manual changes or other automated tools.
"""
        0
    else
        if not (String.IsNullOrEmpty filePath) then
            processFile filePath level
        elif not (String.IsNullOrEmpty dirPath) then
            processDirectory dirPath level
        0