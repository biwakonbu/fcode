#!/usr/bin/env dotnet fsi

(*
F# FSAC Auto-Fix Tool
FSAC診断警告を自動修正するF# Scriptツール

使用方法:
  dotnet fsi fsac-auto-fix.fsx -- --file <filepath>
  dotnet fsi fsac-auto-fix.fsx -- --dir <directory>
  dotnet fsi fsac-auto-fix.fsx -- --dir <directory> --dry-run
*)

open System
open System.IO
open System.Text.RegularExpressions

// 診断情報の型定義
type DiagnosticInfo = {
    Code: string
    Message: string
    Line: int
    StartChar: int
    EndChar: int
    Severity: string
}

type FixResult = {
    OriginalContent: string
    FixedContent: string
    ChangesApplied: int
    Description: string
}

// 修正パターンの型定義
type FixPattern = {
    Code: string
    Name: string
    Pattern: Regex
    Replacement: string -> string
}

// FSAC0004: 不要な括弧の修正パターン
let unnecessaryParenthesesPatterns = [
    // 単純な関数呼び出し: func(arg) -> func arg
    {
        Code = "FSAC0004"
        Name = "Simple function call"
        Pattern = Regex(@"\b(\w+)\((\w+)\)", RegexOptions.Compiled)
        Replacement = fun (content: string) ->
            let pattern = Regex(@"\b(\w+)\((\w+)\)")
            pattern.Replace(content, "$1 $2")
    }
    
    // メソッド呼び出し: obj.Method(arg) -> obj.Method arg
    {
        Code = "FSAC0004" 
        Name = "Method call"
        Pattern = Regex(@"(\w+\.\w+)\((\w+)\)", RegexOptions.Compiled)
        Replacement = fun (content: string) ->
            let pattern = Regex(@"(\w+\.\w+)\((\w+)\)")
            pattern.Replace(content, "$1 $2")
    }
    
    // 静的メソッド呼び出し: Type.Method(arg) -> Type.Method arg
    {
        Code = "FSAC0004"
        Name = "Static method call"
        Pattern = Regex(@"([A-Z]\w*\.[A-Z]\w*)\((\w+)\)", RegexOptions.Compiled)
        Replacement = fun (content: string) ->
            let pattern = Regex(@"([A-Z]\w*\.[A-Z]\w*)\((\w+)\)")
            pattern.Replace(content, "$1 $2")
    }
    
    // キャスト関数呼び出し: float(expr) -> float expr, int(expr) -> int expr
    {
        Code = "FSAC0004"
        Name = "Cast function call"
        Pattern = Regex(@"\b(float|int|string|bool)\s*\(([^)]+)\)", RegexOptions.Compiled)
        Replacement = fun (content: string) ->
            let pattern = Regex(@"\b(float|int|string|bool)\s*\(([^)]+)\)")
            pattern.Replace(content, "$1 $2")
    }
    
    // マッチ文での関数呼び出し: match func(arg) with -> match func arg with
    {
        Code = "FSAC0004"
        Name = "Match expression function call"
        Pattern = Regex(@"match\s+(\w+)\((\w+)\)\s+with", RegexOptions.Compiled)
        Replacement = fun (content: string) ->
            let pattern = Regex(@"match\s+(\w+)\((\w+)\)\s+with")
            pattern.Replace(content, "match $1 $2 with")
    }
    
    // パイプライン内での関数呼び出し: |> func(arg) -> |> func arg  
    {
        Code = "FSAC0004"
        Name = "Pipeline function call"
        Pattern = Regex(@"\|\>\s*(\w+)\((\w+)\)", RegexOptions.Compiled)
        Replacement = fun (content: string) ->
            let pattern = Regex(@"\|\>\s*(\w+)\((\w+)\)")
            pattern.Replace(content, "|> $1 $2")
    }
]

// FSAC0002: 冗長な修飾子の修正パターン
let redundantQualifierPatterns = [
    {
        Code = "FSAC0002"
        Name = "System module qualifiers"
        Pattern = Regex(@"System\.(String|Int32|DateTime|Boolean)\.(\w+)", RegexOptions.Compiled)
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
        Replacement = fun (content: string) ->
            // これは実際の使用解析が必要なので、簡易版として空行で置換
            content // 実装は複雑になるため、手動確認が必要
    }
]

// すべての修正パターン
let allFixPatterns = 
    unnecessaryParenthesesPatterns @ 
    redundantQualifierPatterns @ 
    unusedOpenPatterns

// ファイル内容を修正する
let fixFileContent (content: string) (dryRun: bool) : FixResult =
    let mutable currentContent = content
    let mutable totalChanges = 0
    let mutable descriptions = []
    
    for pattern in unnecessaryParenthesesPatterns do
        let matches = pattern.Pattern.Matches(currentContent)
        if matches.Count > 0 then
            let newContent = pattern.Replacement currentContent
            if newContent <> currentContent then
                descriptions <- $"{pattern.Name}: {matches.Count} fixes" :: descriptions
                totalChanges <- totalChanges + matches.Count
                if not dryRun then
                    currentContent <- newContent
    
    for pattern in redundantQualifierPatterns do
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
let processFile (filePath: string) (dryRun: bool) : unit =
    try
        if not (File.Exists filePath) then
            printfn $"❌ File not found: {filePath}"
            exit 1
            
        let content = File.ReadAllText filePath
        let result = fixFileContent content dryRun
        
        if result.ChangesApplied > 0 then
            let mode = if dryRun then "[DRY RUN]" else "[APPLIED]"
            printfn $"✅ {mode} {filePath}: {result.ChangesApplied} changes"
            if not (String.IsNullOrEmpty result.Description) then
                printfn $"   └─ {result.Description}"
            
            if not dryRun then
                File.WriteAllText(filePath, result.FixedContent)
        else
            printfn $"ℹ️  {filePath}: No changes needed"
            
    with
    | ex ->
        printfn $"❌ Error processing {filePath}: {ex.Message}"

// ディレクトリを再帰的に処理
let processDirectory (dirPath: string) (dryRun: bool) : unit =
    try
        if not (Directory.Exists dirPath) then
            printfn $"❌ Directory not found: {dirPath}"
            exit 1
            
        let fsFiles = 
            Directory.EnumerateFiles(dirPath, "*.fs", SearchOption.AllDirectories)
            |> Seq.filter (fun f -> not (f.Contains "bin" || f.Contains "obj"))
            |> Seq.toArray
            
        printfn $"🔍 Found {fsFiles.Length} F# files in {dirPath}"
        
        let mutable totalChanges = 0
        let mutable processedFiles = 0
        
        for filePath in fsFiles do
            let content = File.ReadAllText filePath
            let result = fixFileContent content dryRun
            
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
        printfn $"📊 Summary: {totalChanges} total changes across {processedFiles} files"
        
        if dryRun then
            printfn "💡 Run without --dry-run to apply changes"
            
    with
    | ex ->
        printfn $"❌ Error processing directory {dirPath}: {ex.Message}"

// コマンドライン引数を解析
let parseArgs (args: string array) : unit =
    let mutable filePath = ""
    let mutable dirPath = ""
    let mutable dryRun = false
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
        | "--help" :: _ | "-h" :: _ ->
            showHelp <- true
        | [] -> ()
        | unknown :: rest ->
            printfn $"⚠️  Unknown argument: {unknown}"
            parseNext rest
    
    parseNext (Array.toList args)
    
    if showHelp || (String.IsNullOrEmpty filePath && String.IsNullOrEmpty dirPath) then
        printfn """
F# FSAC Auto-Fix Tool

Usage:
  dotnet fsi fsac-auto-fix.fsx -- --file <filepath>     # Fix single file
  dotnet fsi fsac-auto-fix.fsx -- --dir <directory>     # Fix all .fs files in directory
  dotnet fsi fsac-auto-fix.fsx -- --dir <dir> --dry-run # Preview changes without applying

Options:
  --file <path>     Process single F# file
  --dir <path>      Process all .fs files in directory recursively
  --dry-run         Preview changes without applying them
  --help, -h        Show this help message

Examples:
  dotnet fsi fsac-auto-fix.fsx -- --file src/Program.fs
  dotnet fsi fsac-auto-fix.fsx -- --dir src/
  dotnet fsi fsac-auto-fix.fsx -- --dir . --dry-run
"""
        exit 0
    
    if not (String.IsNullOrEmpty filePath) then
        processFile filePath dryRun
    elif not (String.IsNullOrEmpty dirPath) then
        processDirectory dirPath dryRun

// メイン実行
match fsi.CommandLineArgs with
| [| _scriptName |] ->
    parseArgs [| "--help" |]
| args ->
    let actualArgs = args |> Array.skip 1 // スクリプト名をスキップ
    parseArgs actualArgs