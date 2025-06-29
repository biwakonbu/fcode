module WorkerProcessManagerDynamicWaitTests

open NUnit.Framework
open System
open System.IO
open System.Threading.Tasks
open FCode.WorkerProcessManager
open FCode.Logger

[<TestFixture>]
type WorkerProcessManagerDynamicWaitTests() =

    let testSocketPath = Path.Combine(Path.GetTempPath(), "test-fcode-socket.sock")

    [<SetUp>]
    member _.Setup() =
        // テスト用ソケットファイルが存在する場合は削除
        if File.Exists(testSocketPath) then
            File.Delete(testSocketPath)

    [<TearDown>]
    member _.TearDown() =
        // テスト後のクリーンアップ
        if File.Exists(testSocketPath) then
            File.Delete(testSocketPath)

    [<Test>]
    member _.``waitForSocketFile - ファイルが存在しない場合のタイムアウト``() =
        task {
            // Arrange
            let nonExistentPath = Path.Combine(Path.GetTempPath(), "non-existent-socket.sock")
            let maxWaitMs = 500 // 500ms待機

            // Act
            let! result = waitForSocketFile nonExistentPath maxWaitMs

            // Assert
            Assert.That(result, Is.False)
        }

    [<Test>]
    member _.``waitForSocketFile - ファイルが作成される場合の成功``() =
        task {
            // Arrange
            let maxWaitMs = 2000 // 2秒待機

            // Act & Assert
            let waitTask = waitForSocketFile testSocketPath maxWaitMs

            // 500ms後にファイルを作成
            Task.Run(
                System.Func<Task>(fun () ->
                    task {
                        do! Task.Delay(500)
                        File.WriteAllText(testSocketPath, "test socket")
                    })
            )
            |> ignore

            let! result = waitTask
            Assert.That(result, Is.True)
        }

    [<Test>]
    member _.``waitForSocketFile - 即座にファイルが存在する場合``() =
        task {
            // Arrange
            File.WriteAllText(testSocketPath, "test socket")
            let maxWaitMs = 1000

            // Act
            let! result = waitForSocketFile testSocketPath maxWaitMs

            // Assert
            Assert.That(result, Is.True)
        }

    [<Test>]
    member _.``waitForIPCConnection - ソケットファイルが存在しない場合``() =
        task {
            // Arrange
            let nonExistentPath = Path.Combine(Path.GetTempPath(), "non-existent-ipc.sock")
            let maxWaitMs = 500

            // Act
            let! result = waitForIPCConnection nonExistentPath maxWaitMs

            // Assert
            Assert.That(result, Is.False)
        }

    [<Test>]
    member _.``waitForIPCConnection - ソケットファイルが存在するが接続不可の場合``() =
        task {
            // Arrange
            File.WriteAllText(testSocketPath, "fake socket") // 実際のソケットではないファイル
            let maxWaitMs = 1000

            // Act
            let! result = waitForIPCConnection testSocketPath maxWaitMs

            // Assert
            Assert.That(result, Is.False) // 接続テストが失敗するはず
        }

    [<Test>]
    member _.``動的待機機能のタイミング測定テスト``() =
        task {
            // Arrange
            let startTime = DateTime.Now
            let maxWaitMs = 1000

            // Act - 存在しないファイルで待機
            let! result = waitForSocketFile "non-existent-path.sock" maxWaitMs

            // Assert
            let elapsed = (DateTime.Now - startTime).TotalMilliseconds
            Assert.That(result, Is.False)
            Assert.That(elapsed, Is.GreaterThanOrEqualTo(float maxWaitMs))
            Assert.That(elapsed, Is.LessThan(float maxWaitMs + 200.0)) // 200ms許容
        }

    [<Test>]
    member _.``ファイル作成タイミングの精度テスト``() =
        task {
            // Arrange
            let startTime = DateTime.Now
            let fileCreationDelayMs = 300
            let maxWaitMs = 1000

            // Act
            let waitTask = waitForSocketFile testSocketPath maxWaitMs

            // 指定時間後にファイルを作成
            Task.Run(
                System.Func<Task>(fun () ->
                    task {
                        do! Task.Delay(fileCreationDelayMs)
                        File.WriteAllText(testSocketPath, "test socket")
                    })
            )
            |> ignore

            let! result = waitTask

            // Assert
            let elapsed = (DateTime.Now - startTime).TotalMilliseconds
            Assert.That(result, Is.True)
            Assert.That(elapsed, Is.GreaterThanOrEqualTo(float fileCreationDelayMs))
            Assert.That(elapsed, Is.LessThan(float fileCreationDelayMs + 200.0)) // 200ms許容
        }

    [<Test>]
    [<Category("Unit")>]
    member _.``waitForSocketFile - 複数同時待機処理テスト``() =
        task {
            // Arrange
            let socket1 = Path.Combine(Path.GetTempPath(), "test-socket-1.sock")
            let socket2 = Path.Combine(Path.GetTempPath(), "test-socket-2.sock")
            let socket3 = Path.Combine(Path.GetTempPath(), "test-socket-3.sock")
            let maxWaitMs = 1500

            // Act - 複数の待機を同時実行
            let task1 = waitForSocketFile socket1 maxWaitMs
            let task2 = waitForSocketFile socket2 maxWaitMs
            let task3 = waitForSocketFile socket3 maxWaitMs

            // 異なるタイミングでファイルを作成
            Task.Run(System.Func<Task>(fun () -> task {
                do! Task.Delay(200)
                File.WriteAllText(socket1, "socket1")
                do! Task.Delay(300)
                File.WriteAllText(socket2, "socket2")
                // socket3は作成しない（タイムアウトテスト）
            })) |> ignore

            let! results = Task.WhenAll([| task1; task2; task3 |])

            // Assert
            Assert.That(results.[0], Is.True, "socket1は作成されるので成功")
            Assert.That(results.[1], Is.True, "socket2は作成されるので成功")
            Assert.That(results.[2], Is.False, "socket3は作成されないのでタイムアウト")

            // Cleanup
            [socket1; socket2; socket3] |> List.iter (fun path ->
                if File.Exists(path) then File.Delete(path))
        }

    [<Test>]
    [<Category("Unit")>]
    member _.``waitForIPCConnection - リトライロジックテスト``() =
        task {
            // Arrange
            let testSocket = Path.Combine(Path.GetTempPath(), "retry-test.sock")
            File.WriteAllText(testSocket, "fake socket") // 接続失敗するファイル
            let maxWaitMs = 3000 // 十分な時間を設定

            // Act
            let startTime = DateTime.Now
            let! result = waitForIPCConnection testSocket maxWaitMs
            let elapsed = (DateTime.Now - startTime).TotalMilliseconds

            // Assert
            Assert.That(result, Is.False, "接続失敗のため false")
            // リトライが発生することを確認（最低でも複数回の試行時間）
            Assert.That(elapsed, Is.GreaterThan(1000.0), "リトライにより1秒以上かかる")

            // Cleanup
            if File.Exists(testSocket) then File.Delete(testSocket)
        }

    [<Test>]
    [<Category("Unit")>]
    member _.``waitForSocketFile - ゼロ待機時間テスト``() =
        task {
            // Arrange
            let nonExistentPath = "zero-wait-test.sock"
            let maxWaitMs = 0 // ゼロ待機

            // Act
            let startTime = DateTime.Now
            let! result = waitForSocketFile nonExistentPath maxWaitMs
            let elapsed = (DateTime.Now - startTime).TotalMilliseconds

            // Assert
            Assert.That(result, Is.False, "即座にfalseを返す")
            Assert.That(elapsed, Is.LessThan(50.0), "50ms以内に完了")
        }

    [<Test>]
    [<Category("Unit")>]
    member _.``waitForSocketFile - 負の待機時間例外処理テスト``() =
        task {
            // Arrange
            let nonExistentPath = "negative-wait-test.sock"
            let maxWaitMs = -100 // 負の値

            // Act & Assert - 負の値でも適切に処理されることを確認
            let! result = waitForSocketFile nonExistentPath maxWaitMs
            Assert.That(result, Is.False, "負の待機時間でも安全に処理")
        }

    [<Test>]
    [<Category("Unit")>]
    member _.``waitForSocketFile - 極端に長い待機時間テスト``() =
        task {
            // Arrange
            let nonExistentPath = "long-wait-test.sock"
            let maxWaitMs = 100000 // 100秒（実際には待機しない）

            // Act
            let startTime = DateTime.Now
            let waitTask = waitForSocketFile nonExistentPath maxWaitMs

            // 短時間でファイルを作成
            Task.Run(System.Func<Task>(fun () -> task {
                do! Task.Delay(100)
                File.WriteAllText(nonExistentPath, "long wait test")
            })) |> ignore

            let! result = waitTask
            let elapsed = (DateTime.Now - startTime).TotalMilliseconds

            // Assert
            Assert.That(result, Is.True, "ファイル作成により成功")
            Assert.That(elapsed, Is.LessThan(1000.0), "実際には短時間で完了")

            // Cleanup
            if File.Exists(nonExistentPath) then File.Delete(nonExistentPath)
        }

    [<Test>]
    [<Category("Unit")>]
    member _.``waitForSocketFile - ファイル削除と再作成の競合状態テスト``() =
        task {
            // Arrange
            let competitionSocket = Path.Combine(Path.GetTempPath(), "competition-test.sock")
            let maxWaitMs = 2000

            // Act
            let waitTask = waitForSocketFile competitionSocket maxWaitMs

            // ファイルの作成・削除・再作成を繰り返す
            Task.Run(System.Func<Task>(fun () -> task {
                do! Task.Delay(200)
                File.WriteAllText(competitionSocket, "temp1")
                do! Task.Delay(100)
                File.Delete(competitionSocket)
                do! Task.Delay(100)
                File.WriteAllText(competitionSocket, "final")
            })) |> ignore

            let! result = waitTask

            // Assert
            Assert.That(result, Is.True, "最終的にファイルが存在するため成功")

            // Cleanup
            if File.Exists(competitionSocket) then File.Delete(competitionSocket)
        }

    [<Test>]
    [<Category("Integration")>]
    member _.``実際のワーカー起動フロー統合テスト``() =
        task {
            // Arrange
            let testPaneId = "integration-test-pane"
            let workingDir = Directory.GetCurrentDirectory()
            let mockTextView = new Terminal.Gui.TextView()

            try
                // Act - 実際のワーカー起動（短時間でタイムアウト）
                let success = workerManager.StartWorker(testPaneId, workingDir, mockTextView)

                // Assert - 起動処理が開始されることを確認
                Assert.That(success, Is.True, "ワーカー起動プロセスが開始される")

                // 少し待機してステータス確認
                do! Task.Delay(1000)
                let isActive = workerManager.IsWorkerActive(testPaneId)
                // 注意: 実際のIPCが利用できない環境では false になる可能性あり
                FCode.Logger.logInfo "DynamicWaitTest" $"Worker active status: {isActive}"

            finally
                // Cleanup
                workerManager.StopWorker(testPaneId) |> ignore
                mockTextView.Dispose()
        }
