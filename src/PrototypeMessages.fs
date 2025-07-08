module FCode.PrototypeMessages

/// プロトタイプモード用のロール別メッセージ
module RoleMessages =

    /// ロール別の初期応答メッセージを取得
    let getRoleResponse (paneId: string) : string =
        match paneId with
        | id when id.StartsWith("qa") ->
            "🔍 QA専門家として準備完了。テスト戦略やバグ検出について相談できます。\n"
            + "現在のプロジェクト状況:\n"
            + "• テストカバレッジ: 240/240テスト成功\n"
            + "• 品質評価: セキュリティ修正完了済み\n"
            + "• 推奨: UI統合テスト実施"
        | id when id.StartsWith("dev") ->
            "💻 シニアエンジニアとして準備完了。技術実装について相談できます。\n"
            + "現在の技術状況:\n"
            + "• F# + Terminal.Gui アーキテクチャ\n"
            + "• Claude Code統合80%完了\n"
            + "• 推奨: I/O統合の最終実装"
        | "ux" ->
            "🎨 UX専門家として準備完了。ユーザビリティについて相談できます。\n"
            + "現在のUX状況:\n"
            + "• 9ペインレイアウト設計完了\n"
            + "• ProgressDashboard統合済み\n"
            + "• 推奨: 操作性改善の検討"
        | "pm" ->
            "📊 PM として準備完了。プロジェクト管理について相談できます。\n"
            + "現在の進捗状況:\n"
            + "• セキュリティ修正: ✅ 完了\n"
            + "• Claude統合: 🟡 80%完了\n"
            + "• 推奨: 基本動作確認を最優先"
        | _ -> "🤖 対話準備完了。プロジェクトについて何でも相談できます。"

/// プロトタイプモード用の疑似応答生成
module ResponseGeneration =

    /// 入力内容に基づく知的疑似応答を生成
    let generatePrototypeResponse (input: string) (paneId: string) : string =
        let lowerInput = input.ToLower().Trim()

        match lowerInput with
        | s when s.Contains("テスト") || s.Contains("test") ->
            "🔍 テストについてですね。現在のプロジェクトでは240/240テストが成功しており、"
            + "セキュリティ修正も完了しています。具体的にどのようなテストを検討していますか？"
        | s when s.Contains("ビルド") || s.Contains("build") ->
            "🔨 ビルドについてですね。F#プロジェクトは正常にビルドされており、"
            + "0警告・0エラーの状態です。dotnet buildコマンドでの実行をお勧めします。"
        | s when s.Contains("実装") || s.Contains("implement") ->
            "💻 実装についてですね。現在Claude Code統合が80%完了しており、"
            + "UI基盤とプロセス管理は完全実装済みです。どの部分の実装を進めますか？"
        | s when s.Contains("設計") || s.Contains("design") ->
            "📐 設計についてですね。9ペインレイアウトとリアルタイム協調アーキテクチャが"
            + "完成しており、Terminal.Gui 1.15.0基盤で安定動作しています。"
        | s when s.Contains("エラー") || s.Contains("error") ->
            "❌ エラーについてですね。現在の実装では包括的エラーハンドリングと" + "自動復旧機能が実装済みです。具体的なエラー内容を教えてください。"
        | s when s.Contains("進捗") || s.Contains("progress") ->
            "📊 進捗についてですね。セキュリティ修正完了、UI基盤完成、" + "Claude統合80%の状況です。次はI/O統合の完成が優先事項です。"
        | s when s.Contains("ヘルプ") || s.Contains("help") ->
            "❓ ヘルプですね。以下のトピックについて相談できます：\n"
            + "• テスト戦略と品質保証\n• 技術実装と設計決定\n• UI/UX改善\n"
            + "• プロジェクト管理と進捗\n具体的に何について知りたいですか？"
        | _ ->
            $"✨ 「{input}」について承知しました。このプロトタイプモードでは、"
            + "実際のClaude AIの代わりにロール別の疑似応答を提供しています。"
            + "Claude CLIがインストールされると完全な対話が可能になります。"

/// プロトタイプモード用の初期メッセージ
module InitialMessages =

    /// プロトタイプセッション開始時の標準メッセージ
    let getStandardMessages (paneId: string) (workingDir: string) (role: string) : string list =
        [ $"[PROTOTYPE] Claude Code プロトタイプモード - ペイン: {paneId}"
          $"[INFO] 作業ディレクトリ: {workingDir}"
          $"[INFO] ロール: {role}"
          "=" + String.replicate 50 "="
          "[INFO] プロトタイプモードで動作中..."
          "[INFO] 実際のClaude CLIがインストールされると完全動作します"
          "" ]

    /// 操作ガイドメッセージ
    let getUsageInstructions () : string list = [ "💡 メッセージを入力してテスト対話を開始してください"; "" ]
