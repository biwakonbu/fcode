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
