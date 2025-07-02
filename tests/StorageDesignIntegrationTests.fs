module FCode.Tests.StorageDesignIntegrationTests

open NUnit.Framework
open FCode.Collaboration.CollaborationTypes
open FCode.Collaboration.TaskStorageFactory
open System
open System.IO

[<TestFixture>]
type StorageDesignIntegrationTests() =

    let mutable tempDbPath = ""
    let mutable connectionString = ""

    [<SetUp>]
    member _.SetUp() =
        tempDbPath <- Path.Combine(Path.GetTempPath(), $"integration_test_{Guid.NewGuid()}.db")
        connectionString <- $"Data Source={tempDbPath};"

    [<TearDown>]
    member _.TearDown() =
        if File.Exists(tempDbPath) then
            File.Delete(tempDbPath)

    [<Test>]
    [<Category("Integration")>]
    member _.``Factory can create both storage designs``() =
        async {
            // 3テーブル設計のテスト
            use threeTableStorage =
                TaskStorageFactory.CreateTaskStorage(connectionString, ThreeTableDesign)

            let! initResult3 = threeTableStorage.InitializeDatabase()

            match initResult3 with
            | Result.Ok(_) -> Assert.Pass()
            | Result.Error(error) -> Assert.Fail($"3-table design initialization failed: {error}")

            // 6テーブル設計のテスト
            let tempDbPath6 =
                Path.Combine(Path.GetTempPath(), $"integration_6table_test_{Guid.NewGuid()}.db")

            let connectionString6 = $"Data Source={tempDbPath6};"

            try
                use sixTableStorage =
                    TaskStorageFactory.CreateTaskStorage(connectionString6, SixTableDesign)

                let! initResult6 = sixTableStorage.InitializeDatabase()

                match initResult6 with
                | Result.Ok(_) -> Assert.Pass()
                | Result.Error(error) -> Assert.Fail($"6-table design initialization failed: {error}")
            finally
                if File.Exists(tempDbPath6) then
                    File.Delete(tempDbPath6)
        }
        |> Async.RunSynchronously

    [<Test>]
    [<Category("Integration")>]
    member _.``Both designs support same TaskInfo CRUD operations``() =
        async {
            let testTask =
                { TaskId = "integration-test-001"
                  Title = "Integration Test Task"
                  Description = "Testing both storage designs"
                  Status = TaskStatus.Pending
                  AssignedAgent = Some "test-agent"
                  Priority = TaskPriority.High
                  EstimatedDuration = Some(TimeSpan.FromMinutes(45.0))
                  ActualDuration = None
                  RequiredResources = [ "cpu"; "memory" ]
                  CreatedAt = DateTime.Now
                  UpdatedAt = DateTime.Now }

            // 3テーブル設計でのテスト
            use threeTableStorage =
                TaskStorageFactory.CreateTaskStorage(connectionString, ThreeTableDesign)

            let! _ = threeTableStorage.InitializeDatabase()
            let! saveResult3 = threeTableStorage.SaveTask(testTask)
            let! getResult3 = threeTableStorage.GetTask("integration-test-001")

            match saveResult3 with
            | Result.Ok(_) -> Assert.Pass()
            | Result.Error(error) -> Assert.Fail($"3-table design save failed: {error}")

            match getResult3 with
            | Result.Ok(Some retrievedTask) ->
                Assert.AreEqual(testTask.TaskId, retrievedTask.TaskId)
                Assert.AreEqual(testTask.Status, retrievedTask.Status)
                Assert.AreEqual(testTask.Priority, retrievedTask.Priority)
            | _ -> Assert.Fail("3-table design should retrieve saved task")

            // 6テーブル設計でのテスト
            let tempDbPath6 =
                Path.Combine(Path.GetTempPath(), $"integration_6table_crud_{Guid.NewGuid()}.db")

            let connectionString6 = $"Data Source={tempDbPath6};"

            try
                use sixTableStorage =
                    TaskStorageFactory.CreateTaskStorage(connectionString6, SixTableDesign)

                let! _ = sixTableStorage.InitializeDatabase()
                let! saveResult6 = sixTableStorage.SaveTask(testTask)
                let! getResult6 = sixTableStorage.GetTask("integration-test-001")

                match saveResult6 with
                | Result.Ok(_) -> Assert.Pass()
                | Result.Error(error) -> Assert.Fail($"6-table design save failed: {error}")

                match getResult6 with
                | Result.Ok(Some retrievedTask) ->
                    Assert.AreEqual(testTask.TaskId, retrievedTask.TaskId)
                    Assert.AreEqual(testTask.Status, retrievedTask.Status)
                    Assert.AreEqual(testTask.Priority, retrievedTask.Priority)
                | _ -> Assert.Fail("6-table design should retrieve saved task")
            finally
                if File.Exists(tempDbPath6) then
                    File.Delete(tempDbPath6)
        }
        |> Async.RunSynchronously

    [<Test>]
    [<Category("Integration")>]
    member _.``Environment variable controls design selection``() =
        // 環境変数設定テスト
        Environment.SetEnvironmentVariable("FCODE_TASK_STORAGE_DESIGN", "3table")
        let design3 = TaskStorageFactory.GetStorageDesignFromEnvironment()
        Assert.AreEqual(ThreeTableDesign, design3)

        Environment.SetEnvironmentVariable("FCODE_TASK_STORAGE_DESIGN", "6table")
        let design6 = TaskStorageFactory.GetStorageDesignFromEnvironment()
        Assert.AreEqual(SixTableDesign, design6)

        Environment.SetEnvironmentVariable("FCODE_TASK_STORAGE_DESIGN", "")
        let designDefault = TaskStorageFactory.GetStorageDesignFromEnvironment()
        Assert.AreEqual(ThreeTableDesign, designDefault, "Default should be 3-table design")

    [<Test>]
    [<Category("Integration")>]
    member _.``Design info provides accurate metrics``() =
        let info3 = TaskStorageFactory.GetDesignInfo(ThreeTableDesign)
        Assert.AreEqual(3, info3.TableCount)
        Assert.AreEqual(7, info3.IndexCount)
        Assert.AreEqual("Low", info3.EstimatedComplexity)

        let info6 = TaskStorageFactory.GetDesignInfo(SixTableDesign)
        Assert.AreEqual(6, info6.TableCount)
        Assert.AreEqual(16, info6.IndexCount)
        Assert.AreEqual("High", info6.EstimatedComplexity)

    [<Test>]
    [<Category("Integration")>]
    member _.``Both designs support progress summary``() =
        async {
            let testTask =
                { TaskId = "progress-test-001"
                  Title = "Progress Test Task"
                  Description = "Testing progress summary"
                  Status = TaskStatus.Completed
                  AssignedAgent = Some "test-agent"
                  Priority = TaskPriority.Medium
                  EstimatedDuration = None
                  ActualDuration = None
                  RequiredResources = []
                  CreatedAt = DateTime.Now
                  UpdatedAt = DateTime.Now }

            // 3テーブル設計のプログレス確認
            use threeTableStorage =
                TaskStorageFactory.CreateTaskStorage(connectionString, ThreeTableDesign)

            let! _ = threeTableStorage.InitializeDatabase()
            let! _ = threeTableStorage.SaveTask(testTask)
            let! progressResult3 = threeTableStorage.GetProgressSummary()

            match progressResult3 with
            | Result.Ok(summary) ->
                Assert.IsTrue(summary.TotalTasks >= 1, "3-table design should report at least 1 total task")
                Assert.IsTrue(summary.CompletedTasks >= 1, "3-table design should report at least 1 completed task")
            | Result.Error(error) -> Assert.Fail($"3-table design progress summary failed: {error}")

        }
        |> Async.RunSynchronously
