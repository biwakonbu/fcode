module FCode.WorkerProcessManager

open System
open System.Diagnostics
open System.IO
open System.Threading.Tasks
open System.Text
open System.Text.Json
open Terminal.Gui
open FCode.Logger
open FCode.ProcessSupervisor
open FCode.UnixDomainSocketManager
open FCode.IPCChannel

// ===============================================
// Worker Process 統合管理
// ===============================================

type WorkerProcessStatus =
    | NotStarted
    | Starting
    | Running
    | Unhealthy
    | Crashed
    | Stopping

type WorkerIO =
    { StandardInput: string -> unit
      StandardOutput: IEvent<string>
      StandardError: IEvent<string>
      IsConnected: bool }

type WorkerProcessInfo =
    { PaneId: string
      ProcessId: int option
      Status: WorkerProcessStatus
      WorkingDirectory: string
      TextView: TextView option
      OutputBuffer: StringBuilder
      StartTime: DateTime
      LastActivity: DateTime
      RestartCount: int
      IO: WorkerIO option }

// ===============================================
// Worker Process Manager
// ===============================================

type WorkerProcessManager() =

    let mutable workers = Map.empty<string, WorkerProcessInfo>
    let supervisor = ProcessSupervisor.supervisor

    // Worker Process 起動
    let startWorkerProcess (paneId: string) (workingDir: string) (textView: TextView) =
        try
            logInfo "WorkerManager" $"Starting worker process for pane: {paneId}"

            // ProcessSupervisor経由でワーカープロセスを起動
            let success = supervisor.StartWorker(paneId, workingDir)

            if success then
                logInfo "WorkerManager" $"Worker process started successfully for pane: {paneId}"

                let outputBuffer = StringBuilder()

                // IPC経由でのI/O統合を設定
                let setupIOIntegration () =
                    task {
                        try
                            // IPCクライアントを作成してワーカープロセスと通信
                            let socketPath = Path.Combine(Path.GetTempPath(), $"fcode-{paneId}.sock")
                            let config = defaultUdsConfig socketPath

                            // ワーカープロセスの起動完了を待機
                            do! Task.Delay(2000) // 2秒待機

                            if File.Exists(socketPath) then
                                let client = new UdsClient(config)
                                let! connection = client.ConnectAsync()
                                logInfo "WorkerManager" $"IPC connection established for pane: {paneId}"

                                // セッション開始コマンドを送信
                                let startSessionCmd = SessionCommand.StartSession(paneId, workingDir)
                                let envelope = createEnvelope startSessionCmd
                                do! connection.SendAsync(envelope)

                                logInfo "WorkerManager" $"Session start command sent for pane: {paneId}"
                                connection.Close()

                            else
                                logWarning "WorkerManager" $"IPC socket not found for pane: {paneId}"

                        with ex ->
                            logException "WorkerManager" $"Error setting up IPC for pane: {paneId}" ex
                    }

                // IPC統合をバックグラウンドで実行
                setupIOIntegration () |> ignore

                // UI更新頻度制限のためのタイマー
                let mutable lastUiUpdate = DateTime.Now
                let uiUpdateThresholdMs = 100 // 100ms間隔制限

                // UI更新用のイベントハンドラー
                let updateUI (data: string) =
                    let now = DateTime.Now

                    if (now - lastUiUpdate).TotalMilliseconds > float uiUpdateThresholdMs then
                        outputBuffer.AppendLine(data) |> ignore
                        textView.Text <- outputBuffer.ToString()
                        textView.SetNeedsDisplay()
                        Application.Refresh()
                        lastUiUpdate <- now

                // 出力イベントの作成
                let outputEvent = Event<string>()
                let errorEvent = Event<string>()

                outputEvent.Publish.Add(fun data ->
                    logDebug $"Worker-{paneId}" $"STDOUT: {data}"
                    updateUI ($"[OUT] {data}"))

                errorEvent.Publish.Add(fun data ->
                    logError $"Worker-{paneId}" $"STDERR: {data}"
                    updateUI ($"[ERR] {data}"))

                // Worker IO の作成
                let workerIO =
                    { StandardInput =
                        fun input ->
                            try
                                // IPC経由で入力を送信
                                let sendInputTask =
                                    task {
                                        try
                                            let command = SessionCommand.SendInput(paneId, input)
                                            let! response = supervisor.SendIPCCommand(command)

                                            match response with
                                            | Some _ ->
                                                logDebug "WorkerManager" $"Input sent via IPC for pane: {paneId}"
                                                outputBuffer.AppendLine($"> {input}") |> ignore
                                                updateUI ("")
                                            | None ->
                                                logError
                                                    "WorkerManager"
                                                    $"Failed to send input via IPC for pane: {paneId}"
                                        with ex ->
                                            logException
                                                "WorkerManager"
                                                $"Error sending input via IPC for pane: {paneId}"
                                                ex
                                    }

                                sendInputTask |> ignore
                            with ex ->
                                logException "WorkerManager" $"Error in StandardInput for pane: {paneId}" ex

                      StandardOutput = outputEvent.Publish
                      StandardError = errorEvent.Publish
                      IsConnected = true }

                let workerInfo =
                    { PaneId = paneId
                      ProcessId = None // ProcessSupervisorから取得する必要があります
                      Status = Starting
                      WorkingDirectory = workingDir
                      TextView = Some textView
                      OutputBuffer = outputBuffer
                      StartTime = DateTime.Now
                      LastActivity = DateTime.Now
                      RestartCount = 0
                      IO = Some workerIO }

                workers <- workers.Add(paneId, workerInfo)

                // 初期メッセージを表示
                outputBuffer.AppendLine($"[DEBUG] Worker Process セッション開始完了 - ペイン: {paneId}")
                |> ignore

                outputBuffer.AppendLine($"[DEBUG] 作業ディレクトリ: {workingDir}") |> ignore
                outputBuffer.AppendLine($"[DEBUG] プロセス分離: 有効") |> ignore
                outputBuffer.AppendLine($"[DEBUG] IPC通信: Unix Domain Socket") |> ignore
                outputBuffer.AppendLine($"[DEBUG] ログファイル: {logger.LogPath}") |> ignore
                outputBuffer.AppendLine("=" + String.replicate 50 "=") |> ignore
                outputBuffer.AppendLine($"[INFO] Worker対話セッション初期化中...") |> ignore
                textView.Text <- outputBuffer.ToString()
                textView.SetNeedsDisplay()
                Application.Refresh()

                logInfo "WorkerManager" $"Worker info created and stored for pane: {paneId}"

                // 初期プロンプトをIPC経由で送信
                Task.Run(
                    System.Func<Task>(fun () ->
                        task {
                            try
                                do! Task.Delay(3000) // ワーカープロセスの完全起動を待機

                                let initPrompt = "こんにちは。Worker Process経由での対話を開始します。現在の作業ディレクトリとプロジェクト状況を教えてください。"
                                workerIO.StandardInput(initPrompt)

                                logInfo "WorkerManager" $"Initial prompt sent via IPC for pane: {paneId}"

                            with ex ->
                                logException "WorkerManager" $"Error sending initial prompt for pane: {paneId}" ex
                        })
                )
                |> ignore

                true
            else
                logError "WorkerManager" $"Failed to start worker process for pane: {paneId}"
                false

        with ex ->
            logException "WorkerManager" $"Exception starting worker for pane: {paneId}" ex
            false

    // Worker Process 停止
    let stopWorkerProcess (paneId: string) =
        try
            logInfo "WorkerManager" $"Stopping worker process for pane: {paneId}"

            match workers.TryFind(paneId) with
            | Some workerInfo ->
                // ProcessSupervisor経由でワーカーを停止
                let success = supervisor.StopWorker(paneId)

                if success then
                    let updatedWorker =
                        { workerInfo with
                            Status = Stopping
                            IO = None }

                    workers <- workers.Add(paneId, updatedWorker)

                    // 終了メッセージを表示
                    match workerInfo.TextView with
                    | Some textView ->
                        workerInfo.OutputBuffer.AppendLine("[INFO] Worker Process セッション終了") |> ignore
                        textView.Text <- workerInfo.OutputBuffer.ToString()
                        textView.SetNeedsDisplay()
                        Application.Refresh()
                    | None -> ()

                    logInfo "WorkerManager" $"Worker process stopped for pane: {paneId}"
                    true
                else
                    logError "WorkerManager" $"Failed to stop worker process for pane: {paneId}"
                    false
            | None ->
                logWarning "WorkerManager" $"Worker not found for stop: {paneId}"
                false

        with ex ->
            logException "WorkerManager" $"Exception stopping worker for pane: {paneId}" ex
            false

    // パブリックメソッド
    member _.StartWorker(paneId: string, workingDir: string, textView: TextView) =
        startWorkerProcess paneId workingDir textView

    member _.StopWorker(paneId: string) = stopWorkerProcess paneId

    member _.SendInput(paneId: string, input: string) =
        match workers.TryFind(paneId) with
        | Some workerInfo when workerInfo.Status = Running ->
            match workerInfo.IO with
            | Some io when io.IsConnected ->
                try
                    io.StandardInput(input)
                    logDebug "WorkerManager" $"Input sent to worker for pane: {paneId}"
                    true
                with ex ->
                    logException "WorkerManager" $"Error sending input to worker for pane: {paneId}" ex
                    false
            | _ ->
                logWarning "WorkerManager" $"Worker IO not available for pane: {paneId}"
                false
        | _ ->
            logWarning "WorkerManager" $"Worker not running for input to pane: {paneId}"
            false

    member _.IsWorkerActive(paneId: string) =
        match workers.TryFind(paneId) with
        | Some workerInfo -> workerInfo.Status = Running || workerInfo.Status = Starting
        | None -> false

    member _.GetWorkerStatus(paneId: string) =
        match workers.TryFind(paneId) with
        | Some workerInfo -> Some workerInfo.Status
        | None -> None

    member _.GetActiveWorkerCount() =
        workers |> Map.filter (fun _ worker -> worker.Status = Running) |> Map.count

    member _.GetAllWorkers() =
        workers |> Map.toList |> List.map (fun (_, worker) -> worker)

    member this.CleanupAllWorkers() =
        workers |> Map.iter (fun paneId _ -> this.StopWorker(paneId) |> ignore)
        workers <- Map.empty

    member _.StartSupervisor() =
        supervisor.StartSupervisor()
        logInfo "WorkerManager" "ProcessSupervisor started"

    member _.StopSupervisor() =
        supervisor.StopSupervisor()
        logInfo "WorkerManager" "ProcessSupervisor stopped"

// Global worker manager instance
let workerManager = WorkerProcessManager()
