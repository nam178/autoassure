using A2.Server.Models;
using A2.Server.Repositories;

namespace A2.Server.UnitTests;

/// <summary>Unit tests for the runs-table row mappers in <c>DynamoDbMapper.Run.cs</c>: the header row,
/// the Scenario snapshot row and the status update row, each converted to a DynamoDB attribute map and
/// back.</summary>
public sealed class DynamoDbMapperRunTests
{
    private static SnapshotSource SampleSource(Guid id) =>
        new()
        {
            Id = id,
            CreatedByUserId = Guid.NewGuid(),
            UpdatedByUserId = Guid.NewGuid(),
            CreatedAt = DateTimeOffset.Parse("2026-01-01T00:00:00Z"),
            UpdatedAt = DateTimeOffset.Parse("2026-01-02T00:00:00Z"),
        };

    private static RunEnvironmentSnapshot SampleEnvironment() =>
        new()
        {
            Source = SampleSource(Guid.NewGuid()),
            Name = "Staging",
            Classification = EnvironmentClassification.NonProduction,
            Variables =
            [
                new RunEnvironmentVariableSnapshot
                {
                    Key = "BASE_URL",
                    Value = "https://staging.example.com",
                    IsSensitive = false,
                    CreatedByUserId = Guid.NewGuid(),
                    UpdatedByUserId = Guid.NewGuid(),
                    CreatedAt = DateTimeOffset.Parse("2026-01-01T00:00:00Z"),
                    UpdatedAt = DateTimeOffset.Parse("2026-01-02T00:00:00Z"),
                },
                new RunEnvironmentVariableSnapshot
                {
                    Key = "API_KEY",
                    Value = "abc.................",
                    IsSensitive = true,
                    CreatedByUserId = Guid.NewGuid(),
                    UpdatedByUserId = Guid.NewGuid(),
                    CreatedAt = DateTimeOffset.Parse("2026-01-01T00:00:00Z"),
                    UpdatedAt = DateTimeOffset.Parse("2026-01-02T00:00:00Z"),
                },
            ],
        };

    private static Run SampleRun(Guid? id = null) =>
        new()
        {
            Id = id ?? Guid.NewGuid(),
            OrganizationId = Guid.NewGuid(),
            ApplicationId = Guid.NewGuid(),
            Trigger = RunTrigger.Manual,
            Status = RunStatus.Running,
            StatusReason = RunStatusReason.HeartbeatLost,
            TotalActivityCount = 10,
            PassedActivityCount = 4,
            FailedActivityCount = 3,
            SkippedActivityCount = 2,
            Environment = SampleEnvironment(),
            LastSeq = 42,
            TriggeredByUserId = Guid.NewGuid(),
            CreatedAt = DateTimeOffset.Parse("2026-01-01T00:00:00Z"),
            StartedAt = DateTimeOffset.Parse("2026-01-01T00:01:00Z"),
            CompletedAt = DateTimeOffset.Parse("2026-01-01T00:10:00Z"),
            LastHeartbeatAt = DateTimeOffset.Parse("2026-01-01T00:09:30Z"),
            DeadlineAt = DateTimeOffset.Parse("2026-01-01T01:00:00Z"),
            ExpiresAt = 1798761600,
        };

    private static RunScenarioSnapshot SampleScenarioSnapshot(Guid scenarioId) =>
        new()
        {
            Source = SampleSource(scenarioId),
            Title = "Checkout flow",
            Description = "Buys an item and pays for it.",
            Folder = "Checkout",
            Tags = ["smoke", "checkout"],
            Activities =
            [
                new RunActivitySnapshot
                {
                    Source = SampleSource(Guid.NewGuid()),
                    Order = 1,
                    Description = "Add item to cart",
                    Preconditions =
                    [
                        new RunPreconditionSnapshot
                        {
                            Source = SampleSource(Guid.NewGuid()),
                            Name = "Order Confirmation ID",
                            ValueSource = PreconditionValueSource.PriorActivity,
                            ExampleValue = "ORD-123",
                        },
                    ],
                    EvidenceDefinitions =
                    [
                        new RunEvidenceDefinitionSnapshot
                        {
                            Source = SampleSource(Guid.NewGuid()),
                            Name = "Cart total",
                            Description = "The total shown on the cart page.",
                            ExampleValue = "$42.00",
                        },
                    ],
                },
            ],
        };

    private static ActivityResult SampleActivityResult(Guid scenarioId, Guid activityId) =>
        new()
        {
            ScenarioId = scenarioId,
            ActivityId = activityId,
            Status = ActivityResultStatus.Passed,
            ResolvedPreconditions = new Dictionary<string, string>
            {
                ["Order Confirmation ID"] = "ORD-123",
            },
            Evidence = new Dictionary<string, string> { ["Cart total"] = "$42.00" },
            ContinuationReasoning = "Retried after a transient 500.",
        };

    // ----- Header row -----

    [Fact]
    public void HeaderRow_WhenRoundTripped_PreservesEveryField()
    {
        // setup
        var run = SampleRun();

        // test
        var row = run.ToDynamoDbRow();
        var roundTripped = row.ToRun();

        // verify
        Assert.Equal(run.Id, roundTripped.Id);
        Assert.Equal(run.OrganizationId, roundTripped.OrganizationId);
        Assert.Equal(run.ApplicationId, roundTripped.ApplicationId);
        Assert.Equal(run.Trigger, roundTripped.Trigger);
        Assert.Equal(run.Status, roundTripped.Status);
        Assert.Equal(run.StatusReason, roundTripped.StatusReason);
        Assert.Equal(run.TotalActivityCount, roundTripped.TotalActivityCount);
        Assert.Equal(run.PassedActivityCount, roundTripped.PassedActivityCount);
        Assert.Equal(run.FailedActivityCount, roundTripped.FailedActivityCount);
        Assert.Equal(run.SkippedActivityCount, roundTripped.SkippedActivityCount);
        Assert.Equivalent(run.Environment, roundTripped.Environment, strict: true);
        Assert.Equal(run.LastSeq, roundTripped.LastSeq);
        Assert.Equal(run.TriggeredByUserId, roundTripped.TriggeredByUserId);
        Assert.Equal(run.CreatedAt, roundTripped.CreatedAt);
        Assert.Equal(run.StartedAt, roundTripped.StartedAt);
        Assert.Equal(run.CompletedAt, roundTripped.CompletedAt);
        Assert.Equal(run.LastHeartbeatAt, roundTripped.LastHeartbeatAt);
        Assert.Equal(run.DeadlineAt, roundTripped.DeadlineAt);
        Assert.Equal(run.ExpiresAt, roundTripped.ExpiresAt);
    }

    [Fact]
    public void HeaderRow_WhenOptionalFieldsAreNull_MapsToAbsentAttributesAndBackToNull()
    {
        // setup: a freshly-Pending run has none of the fields that only get set once execution starts.
        var run = new Run
        {
            Id = Guid.NewGuid(),
            OrganizationId = Guid.NewGuid(),
            ApplicationId = Guid.NewGuid(),
            Trigger = RunTrigger.Scheduled,
            Status = RunStatus.Pending,
            StatusReason = null,
            Environment = SampleEnvironment(),
            TriggeredByUserId = null,
            CreatedAt = DateTimeOffset.Parse("2026-01-01T00:00:00Z"),
            StartedAt = null,
            CompletedAt = null,
            LastHeartbeatAt = null,
            DeadlineAt = null,
            ExpiresAt = null,
        };

        // test
        var row = run.ToDynamoDbRow();

        // verify: absent from the row, not present-with-a-null-marker.
        Assert.False(row.ContainsKey("StatusReason"));
        Assert.False(row.ContainsKey("TriggeredByUserId"));
        Assert.False(row.ContainsKey("StartedAt"));
        Assert.False(row.ContainsKey("CompletedAt"));
        Assert.False(row.ContainsKey("LastHeartbeatAt"));
        Assert.False(row.ContainsKey("DeadlineAt"));
        Assert.False(row.ContainsKey("ExpiresAt"));
        Assert.False(row.ContainsKey("InFlightShard"));

        var roundTripped = row.ToRun();
        Assert.Null(roundTripped.StatusReason);
        Assert.Null(roundTripped.TriggeredByUserId);
        Assert.Null(roundTripped.StartedAt);
        Assert.Null(roundTripped.CompletedAt);
        Assert.Null(roundTripped.LastHeartbeatAt);
        Assert.Null(roundTripped.DeadlineAt);
        Assert.Null(roundTripped.ExpiresAt);
    }

    [Fact]
    public void HeaderRow_WhenMapped_AlwaysCarriesHeaderId()
    {
        // setup
        var run = SampleRun();

        // test
        var row = run.ToDynamoDbRow();

        // verify
        Assert.True(row.ContainsKey("HeaderId"));
        Assert.Equal(run.Id.ToString(), row["HeaderId"].S);
    }

    [Fact]
    public void HeaderRow_WhenRunning_CarriesInFlightShard()
    {
        // setup
        var run = SampleRun() with
        {
            Status = RunStatus.Running,
        };

        // test
        var row = run.ToDynamoDbRow();

        // verify
        Assert.True(row.ContainsKey("InFlightShard"));
        Assert.Equal(DynamoDbMapper.RunInFlightShard(run.Id), row["InFlightShard"].S);
        Assert.StartsWith("Running#", row["InFlightShard"].S);
    }

    [Theory]
    [InlineData(RunStatus.Pending)]
    [InlineData(RunStatus.Completed)]
    [InlineData(RunStatus.Cancelled)]
    [InlineData(RunStatus.Abandoned)]
    public void HeaderRow_WhenNotRunning_DoesNotCarryInFlightShard(RunStatus status)
    {
        // setup
        var run = SampleRun() with
        {
            Status = status,
        };

        // test
        var row = run.ToDynamoDbRow();

        // verify
        Assert.False(row.ContainsKey("InFlightShard"));
    }

    [Fact]
    public void HeaderRow_WhenMapped_UsesTheRunIdAloneAsItsRowKey()
    {
        // setup
        var run = SampleRun();

        // test
        var row = run.ToDynamoDbRow();

        // verify
        Assert.Equal(run.Id.ToString(), row["RowKey"].S);
        Assert.Equal(DynamoDbMapper.RunHeaderRowKey(run.Id), row["RowKey"].S);
    }

    // ----- Scenario snapshot row -----

    [Fact]
    public void ScenarioRow_WhenRoundTripped_PreservesEveryField()
    {
        // setup
        var runId = Guid.NewGuid();
        var organizationId = Guid.NewGuid();
        var applicationId = Guid.NewGuid();
        var snapshot = SampleScenarioSnapshot(Guid.NewGuid());

        // test
        var row = snapshot.ToDynamoDbRow(runId, organizationId, applicationId, 1798761600);
        var roundTripped = row.ToRunScenarioSnapshot();

        // verify
        Assert.Equivalent(snapshot, roundTripped, strict: true);
        Assert.Equal(runId.ToString(), row["RunId"].S);
        Assert.Equal(organizationId.ToString(), row["OrganizationId"].S);
        Assert.Equal(applicationId.ToString(), row["ApplicationId"].S);
        Assert.Equal(snapshot.Source.Id.ToString(), row["ScenarioId"].S);
        Assert.Equal("1798761600", row["ExpiresAt"].N);
    }

    [Fact]
    public void ScenarioRow_WhenMapped_UsesTheRunAndScenarioIdAsItsRowKey()
    {
        // setup
        var runId = Guid.NewGuid();
        var scenarioId = Guid.NewGuid();
        var snapshot = SampleScenarioSnapshot(scenarioId);

        // test
        var row = snapshot.ToDynamoDbRow(runId, Guid.NewGuid(), Guid.NewGuid(), null);

        // verify
        Assert.Equal($"{runId}#scenario#{scenarioId}", row["RowKey"].S);
        Assert.Equal(DynamoDbMapper.RunScenarioRowKey(runId, scenarioId), row["RowKey"].S);
    }

    [Fact]
    public void ScenarioRow_WhenMapped_NeverCarriesHeaderId()
    {
        // setup
        var snapshot = SampleScenarioSnapshot(Guid.NewGuid());

        // test
        var row = snapshot.ToDynamoDbRow(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null);

        // verify: only a header row carries HeaderId -- see the sparse RunHeaderIndex.
        Assert.False(row.ContainsKey("HeaderId"));
    }

    [Fact]
    public void ScenarioRow_WhenExpiresAtIsNull_MapsToAnAbsentAttributeAndBackToNull()
    {
        // setup
        var snapshot = SampleScenarioSnapshot(Guid.NewGuid());

        // test
        var row = snapshot.ToDynamoDbRow(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null);

        // verify
        Assert.False(row.ContainsKey("ExpiresAt"));
    }

    // ----- Status update row -----

    [Fact]
    public void StatusUpdateRow_WhenRoundTripped_PreservesEveryField()
    {
        // setup
        var runId = Guid.NewGuid();
        var organizationId = Guid.NewGuid();
        var applicationId = Guid.NewGuid();
        var update = new RunStatusUpdate
        {
            Seq = 7,
            Kind = RunStatusUpdateKind.AppendActivityResult,
            CreatedAt = DateTimeOffset.Parse("2026-01-01T00:05:00Z"),
            ActivityResult = SampleActivityResult(Guid.NewGuid(), Guid.NewGuid()),
        };

        // test
        var row = update.ToDynamoDbRow(runId, organizationId, applicationId, 1798761600);
        var roundTripped = row.ToRunStatusUpdate();

        // verify
        Assert.Equivalent(update, roundTripped, strict: true);
        Assert.Equal(runId.ToString(), row["RunId"].S);
        Assert.Equal(organizationId.ToString(), row["OrganizationId"].S);
        Assert.Equal(applicationId.ToString(), row["ApplicationId"].S);
        Assert.Equal("1798761600", row["ExpiresAt"].N);
    }

    [Fact]
    public void StatusUpdateRow_WhenContinuationReasoningIsNull_MapsToAnAbsentAttributeAndBackToNull()
    {
        // setup
        var update = new RunStatusUpdate
        {
            Seq = 1,
            Kind = RunStatusUpdateKind.AppendActivityResult,
            CreatedAt = DateTimeOffset.UtcNow,
            ActivityResult = new ActivityResult
            {
                ScenarioId = Guid.NewGuid(),
                ActivityId = Guid.NewGuid(),
                Status = ActivityResultStatus.Failed,
                ContinuationReasoning = null,
            },
        };

        // test
        var row = update.ToDynamoDbRow(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null);
        var roundTripped = row.ToRunStatusUpdate();

        // verify
        Assert.False(row["ActivityResult"].M.ContainsKey("ContinuationReasoning"));
        Assert.Null(roundTripped.ActivityResult?.ContinuationReasoning);
        Assert.Empty(roundTripped.ActivityResult!.ResolvedPreconditions);
        Assert.Empty(roundTripped.ActivityResult!.Evidence);
    }

    [Fact]
    public void StatusUpdateRow_WhenMapped_NeverCarriesHeaderId()
    {
        // setup
        var update = new RunStatusUpdate
        {
            Seq = 1,
            Kind = RunStatusUpdateKind.AppendActivityResult,
            CreatedAt = DateTimeOffset.UtcNow,
            ActivityResult = SampleActivityResult(Guid.NewGuid(), Guid.NewGuid()),
        };

        // test
        var row = update.ToDynamoDbRow(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null);

        // verify
        Assert.False(row.ContainsKey("HeaderId"));
    }

    [Fact]
    public void StatusUpdateRow_WhenMapped_UsesTheRunIdAndPaddedSeqAsItsRowKey()
    {
        // setup
        var runId = Guid.NewGuid();
        var update = new RunStatusUpdate
        {
            Seq = 9,
            Kind = RunStatusUpdateKind.AppendActivityResult,
            CreatedAt = DateTimeOffset.UtcNow,
            ActivityResult = SampleActivityResult(Guid.NewGuid(), Guid.NewGuid()),
        };

        // test
        var row = update.ToDynamoDbRow(runId, Guid.NewGuid(), Guid.NewGuid(), null);

        // verify
        Assert.Equal($"{runId}#update#000000000009", row["RowKey"].S);
        Assert.Equal(DynamoDbMapper.RunStatusUpdateRowKey(runId, 9), row["RowKey"].S);
    }

    // ----- The padding function, and why it matters -----

    [Fact]
    public void RunStatusUpdateRowKey_WhenSequencesAreSortedAsStrings_SortsInNumericOrder()
    {
        // setup: 1, 9, 10 and 100 built into row keys for the same run. An unpadded scheme would sort
        // "#update#10" before "#update#9" as plain strings, silently reordering a poll cursor's results
        // the moment a run passes nine updates.
        var runId = Guid.NewGuid();
        long[] sequencesInCreationOrder = [1, 9, 10, 100];
        var keys = sequencesInCreationOrder
            .Select(seq => DynamoDbMapper.RunStatusUpdateRowKey(runId, seq))
            .ToList();

        // test: sort the keys the same way DynamoDB compares range keys -- as plain strings.
        var sortedByStringComparison = keys.OrderBy(key => key, StringComparer.Ordinal).ToList();

        // verify: the string sort recovers exactly the creation (numeric) order.
        Assert.Equal(keys, sortedByStringComparison);
    }

    [Fact]
    public void RunStatusUpdateRowKey_WhenGivenASequenceNumber_PadsItToTwelveDigits()
    {
        // setup
        var runId = Guid.NewGuid();

        // test
        var key = DynamoDbMapper.RunStatusUpdateRowKey(runId, 42);

        // verify
        Assert.Equal($"{runId}#update#000000000042", key);
    }
}
