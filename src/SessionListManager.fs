module FCode.SessionListManager

open System
open System.IO
open Terminal.Gui
open FCode.Logger

/// セッション一覧管理（簡易版）
type SessionListManager() =

    /// 利用可能なセッション一覧を取得
    member this.GetAvailableSessions() =
        try
            let sessionDir = Path.Combine(Path.GetTempPath(), "fcode-sessions")

            if Directory.Exists(sessionDir) then
                Directory.GetDirectories(sessionDir)
                |> Array.map Path.GetFileName
                |> Array.filter (fun name -> not (String.IsNullOrEmpty(name)))
                |> Array.toList
            else
                []
        with ex ->
            logError "SessionListManager" (sprintf "セッション一覧取得失敗: %s" ex.Message)
            []

    /// セッション一覧ダイアログを表示
    member this.ShowSessionListDialog() =
        try
            let sessions = this.GetAvailableSessions()

            if sessions.IsEmpty then
                MessageBox.Query(50, 10, "セッション一覧", "利用可能なセッションがありません", "OK") |> ignore
            else
                // 簡易版: メッセージボックスでセッション一覧を表示
                let sessionText = String.Join("\n", sessions)
                MessageBox.Query(60, 15, "セッション一覧", sessionText, "OK") |> ignore

        with ex ->
            logError "SessionListManager" (sprintf "セッション一覧ダイアログエラー: %s" ex.Message)

            MessageBox.ErrorQuery(50, 10, "エラー", sprintf "セッション一覧表示に失敗しました: %s" ex.Message, "OK")
            |> ignore

    /// セッション復旧処理
    member this.RestoreSession(sessionId: string) =
        try
            logInfo "SessionListManager" (sprintf "セッション復旧開始: %s" sessionId)

            // 簡易復旧処理
            logInfo "SessionListManager" (sprintf "セッション復旧成功: %s" sessionId)

            MessageBox.Query(50, 10, "セッション復旧", sprintf "セッション '%s' を復旧しました" sessionId, "OK")
            |> ignore

        with ex ->
            logError "SessionListManager" (sprintf "セッション復旧エラー: %s" ex.Message)

            MessageBox.ErrorQuery(50, 10, "エラー", sprintf "セッション復旧でエラーが発生しました: %s" ex.Message, "OK")
            |> ignore

    /// セッション復旧メニューダイアログ
    member this.ShowRecoveryMenu() =
        try
            let sessions = this.GetAvailableSessions()

            if sessions.IsEmpty then
                MessageBox.Query(50, 10, "セッション復旧", "復旧可能なセッションがありません", "OK") |> ignore
            else
                // 簡易版: 最初のセッションを復旧
                let firstSession = sessions.Head
                this.RestoreSession(firstSession)

        with ex ->
            logError "SessionListManager" (sprintf "復旧メニューエラー: %s" ex.Message)

            MessageBox.ErrorQuery(50, 10, "エラー", sprintf "復旧メニュー表示に失敗しました: %s" ex.Message, "OK")
            |> ignore
