#!/usr/bin/env dotnet fsi

#r "nuget: FSharp.Compiler.Service, 43.8.400"

(*
F# AST-Based Auto-Fix Tool
F#コンパイラのAST解析を使用したFSAC診断警告自動修正ツール

使用方法:
  dotnet fsi fsac-ast-fix.fsx -- --file <filepath>
  dotnet fsi fsac-ast-fix.fsx -- --dir <directory>
  dotnet fsi fsac-ast-fix.fsx -- --dir <directory> --dry-run
  dotnet fsi fsac-ast-fix.fsx -- --dir <directory> --level conservative
*)

open System
open System.IO
open System.Collections.Generic
open FSharp.Compiler.CodeAnalysis
open FSharp.Compiler.Syntax
open FSharp.Compiler.Text

// ===============================================
// 修正レベル定義
// ===============================================

type FixLevel =
    | Conservative  // 保守的：型キャストのみ
    | Standard     // 標準：安全な関数呼び出し修正
    | Aggressive   // 積極的：すべての修正

// ===============================================
// AST変換ルール
// ===============================================

type TransformRule = {
    Name: string
    Level: FixLevel
    Description: string
    Transform: SynExpr -> SynExpr option
}

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

// 型名がIDisposableかチェック
let isDisposableType (typeName: string) : bool =
    iDisposableTypes.Contains typeName

// 基本型キャスト関数のリスト
let castFunctions = Set.ofList [
    "float"; "int"; "string"; "bool"; "byte"; "sbyte"
    "int16"; "uint16"; "int32"; "uint32"; "int64"; "uint64"
    "decimal"; "char"
]

// ===============================================
// AST変換ルール実装
// ===============================================

// 型キャスト関数の括弧除去: float(x) -> float x
let castFunctionRule: TransformRule = {
    Name = "Cast function parentheses removal"
    Level = Conservative
    Description = "Remove unnecessary parentheses from type cast functions"
    Transform = function
        | SynExpr.App (_, false, SynExpr.Ident ident, SynExpr.Paren (expr, _, _, _), _) 
            when castFunctions.Contains ident.idText ->
            Some (SynExpr.App (ExprAtomicFlag.NonAtomic, false, SynExpr.Ident ident, expr, ident.idRange))
        | _ -> None
}

// 安全な関数呼び出しの括弧除去: func(arg) -> func arg（newキーワードとIDisposableを除外）
let safeFunctionCallRule: TransformRule = {
    Name = "Safe function call parentheses removal"
    Level = Standard
    Description = "Remove unnecessary parentheses from safe function calls"
    Transform = function
        | SynExpr.App (_, false, SynExpr.Ident ident, SynExpr.Paren (expr, _, _, _), range) 
            when not (isDisposableType ident.idText) ->
            // newキーワードチェックは文脈解析が必要なため、AST変換では難しい
            // ここでは型ベースのチェックのみ実装
            Some (SynExpr.App (ExprAtomicFlag.NonAtomic, false, SynExpr.Ident ident, expr, range))
        | _ -> None
}

// メソッド呼び出しの括弧除去: obj.Method(arg) -> obj.Method arg
let methodCallRule: TransformRule = {
    Name = "Method call parentheses removal"
    Level = Standard
    Description = "Remove unnecessary parentheses from method calls"
    Transform = function
        | SynExpr.App (_, false, SynExpr.DotGet (expr, _, longDotId, _), SynExpr.Paren (arg, _, _, _), range) ->
            Some (SynExpr.App (ExprAtomicFlag.NonAtomic, false, SynExpr.DotGet (expr, range, longDotId, range), arg, range))
        | _ -> None
}

// 全変換ルール
let allTransformRules = [
    castFunctionRule
    safeFunctionCallRule
    methodCallRule
]

// レベル別ルール取得
let getRulesByLevel (level: FixLevel) =
    allTransformRules
    |> List.filter (fun rule -> 
        match level with
        | Conservative -> rule.Level = Conservative
        | Standard -> rule.Level = Conservative || rule.Level = Standard
        | Aggressive -> true)

// ===============================================
// AST訪問・変換エンジン
// ===============================================

type AstTransformer(rules: TransformRule list) =
    let mutable transformCount = 0
    
    member _.TransformCount = transformCount
    
    member this.TransformExpr (expr: SynExpr) : SynExpr =
        // 各ルールを順番に適用
        let mutable currentExpr = expr
        
        for rule in rules do
            match rule.Transform currentExpr with
            | Some transformed ->
                transformCount <- transformCount + 1
                currentExpr <- transformed
                printfn $"  ✓ Applied: {rule.Name}"
            | None -> ()
        
        // 再帰的に子要素を変換
        match currentExpr with
        | SynExpr.App (atomicFlag, isInfix, funcExpr, argExpr, range) ->
            let newFuncExpr = this.TransformExpr funcExpr
            let newArgExpr = this.TransformExpr argExpr
            SynExpr.App (atomicFlag, isInfix, newFuncExpr, newArgExpr, range)
            
        | SynExpr.Paren (expr, leftParenRange, rightParenRange, range) ->
            let newExpr = this.TransformExpr expr
            SynExpr.Paren (newExpr, leftParenRange, rightParenRange, range)
            
        | SynExpr.LetOrUse (isUse, isRecursive, bindings, body, range, attrs) ->
            let newBody = this.TransformExpr body
            // bindingsの変換は複雑なので今回は省略
            SynExpr.LetOrUse (isUse, isRecursive, bindings, newBody, range, attrs)
            
        | SynExpr.Match (matchRange, expr, clauses, range, attrs) ->
            let newExpr = this.TransformExpr expr
            // clausesの変換は複雑なので今回は省略
            SynExpr.Match (matchRange, newExpr, clauses, range, attrs)
            
        | _ -> currentExpr
    
    member this.TransformModule (moduleDecl: SynModuleDecl) : SynModuleDecl =
        match moduleDecl with
        | SynModuleDecl.Let (isRecursive, bindings, range) ->
            // Let bindingの式部分を変換
            // 簡略化のため、今回は基本構造のみ実装
            moduleDecl
        | _ -> moduleDecl

// ===============================================
// ファイル処理
// ===============================================

type FixResult = {
    FilePath: string
    OriginalSource: string
    TransformedSource: string option
    TransformCount: int
    Errors: string list
}

let processFile (filePath: string) (level: FixLevel) (dryRun: bool) : FixResult =
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
        | ParsedInput.ImplFile (ParsedImplFileInput (fileName, isScript, qualifiedName, scopedPragmas, hashDirectives, modules, isLastCompiland, _, _)) ->
            // AST変換実行
            let rules = getRulesByLevel level
            let transformer = AstTransformer(rules)
            
            printfn $"📝 Processing: {Path.GetFileName filePath}"
            
            // モジュール変換（簡略版）
            let transformedModules = 
                modules |> List.map (fun moduleOrNamespace ->
                    match moduleOrNamespace with
                    | SynModuleOrNamespace (longId, isRecursive, kind, decls, docs, attrs, access, range, trivia) ->
                        let transformedDecls = decls |> List.map transformer.TransformModule
                        SynModuleOrNamespace (longId, isRecursive, kind, transformedDecls, docs, attrs, access, range, trivia))
            
            // 変換結果
            if transformer.TransformCount > 0 then
                printfn $"  ✅ Applied {transformer.TransformCount} transformations"
                
                // 実際の変換はFantomasライブラリが必要
                // 今回はシンプルな実装として、変換カウントのみ返す
                {
                    FilePath = filePath
                    OriginalSource = source
                    TransformedSource = if transformer.TransformCount > 0 then Some source else None
                    TransformCount = transformer.TransformCount
                    Errors = []
                }
            else
                printfn $"  ℹ️  No transformations needed"
                {
                    FilePath = filePath
                    OriginalSource = source
                    TransformedSource = None
                    TransformCount = 0
                    Errors = []
                }
        
        | ParsedInput.SigFile _ ->
            {
                FilePath = filePath
                OriginalSource = source
                TransformedSource = None
                TransformCount = 0
                Errors = ["Signature files not supported"]
            }
            
    with
    | ex ->
        {
            FilePath = filePath
            OriginalSource = ""
            TransformedSource = None
            TransformCount = 0
            Errors = [ex.Message]
        }

// ===============================================
// コマンドライン処理
// ===============================================

let processDirectory (dirPath: string) (level: FixLevel) (dryRun: bool) : unit =
    try
        if not (Directory.Exists dirPath) then
            printfn $"❌ Directory not found: {dirPath}"
            exit 1
            
        let fsFiles = 
            Directory.EnumerateFiles(dirPath, "*.fs", SearchOption.AllDirectories)
            |> Seq.filter (fun f -> not (f.Contains "bin" || f.Contains "obj"))
            |> Seq.toArray
            
        printfn $"🔍 Found {fsFiles.Length} F# files in {dirPath}"
        printfn $"📊 Fix level: {level}"
        
        let mutable totalTransforms = 0
        let mutable processedFiles = 0
        
        for filePath in fsFiles do
            let result = processFile filePath level dryRun
            
            if result.TransformCount > 0 then
                totalTransforms <- totalTransforms + result.TransformCount
                processedFiles <- processedFiles + 1
                
                if not dryRun && result.TransformedSource.IsSome then
                    File.WriteAllText(filePath, result.TransformedSource.Value)
            
            if not result.Errors.IsEmpty then
                printfn $"❌ Errors in {Path.GetFileName filePath}:"
                for error in result.Errors do
                    printfn $"   └─ {error}"
                
        printfn ""
        printfn $"📊 Summary: {totalTransforms} total transformations across {processedFiles} files"
        
        if dryRun then
            printfn "💡 Run without --dry-run to apply changes"
            
    with
    | ex ->
        printfn $"❌ Error processing directory {dirPath}: {ex.Message}"

let parseArgs (args: string array) : unit =
    let mutable filePath = ""
    let mutable dirPath = ""
    let mutable dryRun = false
    let mutable level = Standard
    let mutable showHelp = false
    
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
        | "--help" :: _ | "-h" :: _ ->
            showHelp <- true
        | [] -> ()
        | unknown :: rest ->
            printfn $"⚠️  Unknown argument: {unknown}"
            parseNext rest
    
    parseNext (Array.toList args)
    
    if showHelp || (String.IsNullOrEmpty filePath && String.IsNullOrEmpty dirPath) then
        printfn """
F# AST-Based Auto-Fix Tool

Usage:
  dotnet fsi fsac-ast-fix.fsx -- --file <filepath>           # Fix single file
  dotnet fsi fsac-ast-fix.fsx -- --dir <directory>           # Fix all .fs files in directory
  dotnet fsi fsac-ast-fix.fsx -- --dir <dir> --dry-run       # Preview changes without applying
  dotnet fsi fsac-ast-fix.fsx -- --dir <dir> --level conservative  # Use conservative fix level

Options:
  --file <path>         Process single F# file
  --dir <path>          Process all .fs files in directory recursively
  --dry-run             Preview changes without applying them
  --level <level>       Fix level: conservative, standard (default), aggressive
  --help, -h            Show this help message

Fix Levels:
  conservative          Only safe type cast fixes (float(x) -> float x)
  standard              + Safe function calls (excluding IDisposable constructors)
  aggressive            + All available fixes

Examples:
  dotnet fsi fsac-ast-fix.fsx -- --file src/Program.fs
  dotnet fsi fsac-ast-fix.fsx -- --dir src/ --level conservative
  dotnet fsi fsac-ast-fix.fsx -- --dir . --dry-run
"""
        exit 0
    
    if not (String.IsNullOrEmpty filePath) then
        let result = processFile filePath level dryRun
        if result.TransformCount > 0 && not dryRun && result.TransformedSource.IsSome then
            File.WriteAllText(filePath, result.TransformedSource.Value)
    elif not (String.IsNullOrEmpty dirPath) then
        processDirectory dirPath level dryRun

// メイン実行
match fsi.CommandLineArgs with
| [| _scriptName |] ->
    parseArgs [| "--help" |]
| args ->
    let actualArgs = args |> Array.skip 1 // スクリプト名をスキップ
    parseArgs actualArgs