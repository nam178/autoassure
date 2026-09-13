using A2.Server.Models;
using A2.Server.Repositories;

namespace A2.Server.UnitTests;

/// <summary>Unit tests for the runs-table row mappers in <c>DynamoDbMapper.Run.cs</c>: the header row,
/// the Scenario snapshot row and the status update row, each converted to a DynamoDB attribute map and
/// back.</summary>
public sealed class DynamoDbMapperRunTests
{
    private static RunSnapshotSource SampleSource(Guid id) =>
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
            Scenarios = [SampleScenarioSnapshot(Guid.NewGuid())],
            LastStatusUpdateSequenceNumber = 42,
            TriggeredByUserId = Guid.NewGuid(),
            CreatedAt = DateTimeOffset.Parse("2026-01-01T00:00:00Z"),
            StartedAt = DateTimeOffset.Parse("2026-01-01T00:01:00Z"),
            CompletedAt = DateTimeOffset.Parse("2026-01-01T00:10:00Z"),
            LastHeartbeatAt = DateTimeOffset.Parse("2026-01-01T00:09:30Z"),
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
            ResolvedPreconditions = new Dictionary<Guid, string> { [Guid.NewGuid()] = "ORD-123" },
            Evidence = new Dictionary<Guid, string> { [Guid.NewGuid()] = "$42.00" },
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
        var roundTripped = row.ToRun(run.Environment, run.Scenarios);

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
        Assert.Equal(
            run.LastStatusUpdateSequenceNumber,
            roundTripped.LastStatusUpdateSequenceNumber
        );
        Assert.Equal(run.TriggeredByUserId, roundTripped.TriggeredByUserId);
        Assert.Equal(run.CreatedAt, roundTripped.CreatedAt);
        Assert.Equal(run.StartedAt, roundTripped.StartedAt);
        Assert.Equal(run.CompletedAt, roundTripped.CompletedAt);
        Assert.Equal(run.LastHeartbeatAt, roundTripped.LastHeartbeatAt);
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
            Scenarios = [SampleScenarioSnapshot(Guid.NewGuid())],
            TriggeredByUserId = null,
            CreatedAt = DateTimeOffset.Parse("2026-01-01T00:00:00Z"),
            StartedAt = null,
            CompletedAt = null,
            LastHeartbeatAt = null,
        };

        // test
        var row = run.ToDynamoDbRow();

        // verify: absent from the row, not present-with-a-null-marker.
        Assert.False(row.ContainsKey("StatusReason"));
        Assert.False(row.ContainsKey("TriggeredByUserId"));
        Assert.False(row.ContainsKey("StartedAt"));
        Assert.False(row.ContainsKey("CompletedAt"));
        Assert.False(row.ContainsKey("LastHeartbeatAt"));

        var roundTripped = row.ToRun(run.Environment, run.Scenarios);
        Assert.Null(roundTripped.StatusReason);
        Assert.Null(roundTripped.TriggeredByUserId);
        Assert.Null(roundTripped.StartedAt);
        Assert.Null(roundTripped.CompletedAt);
        Assert.Null(roundTripped.LastHeartbeatAt);
    }

    [Fact]
    public void HeaderRow_WhenMapped_CarriesNeitherEnvironmentNorScenarios()
    {
        // setup: a Run holds both, but each belongs on a row of its own -- keeping them off the header is
        // what makes the header cheap enough to rewrite on every Start Run / Update Run Heart Beat.
        var run = SampleRun();

        // test
        var row = run.ToDynamoDbRow();

        // verify
        Assert.False(row.ContainsKey("Environment"));
        Assert.False(row.ContainsKey("Scenarios"));
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
    public void HeaderRow_WhenMapped_UsesTheRunIdWithA1000SuffixAsItsRowKey()
    {
        // setup
        var run = SampleRun();

        // test
        var row = run.ToDynamoDbRow();

        // verify
        Assert.Equal($"{run.Id}#1000", row["RowKey"].S);
        Assert.Equal(DynamoDbMapper.RunHeaderRowKey(run.Id), row["RowKey"].S);
    }

    // ----- RunningRuns row -----

    [Fact]
    public void RunningRunRow_WhenRoundTripped_PreservesEveryField()
    {
        // setup
        var runningRun = new RunningRun
        {
            Id = Guid.NewGuid(),
            StartedAt = DateTimeOffset.Parse("2026-01-01T00:01:00Z"),
        };

        // test
        var row = runningRun.ToDynamoDbRow(Guid.NewGuid(), Guid.NewGuid());
        var roundTripped = row.ToRunningRun();

        // verify
        Assert.Equal(runningRun.Id, roundTripped.Id);
        Assert.Equal(runningRun.StartedAt, roundTripped.StartedAt);
    }

    // ----- Environment snapshot row -----

    [Fact]
    public void EnvironmentRow_WhenRoundTripped_PreservesEveryField()
    {
        // setup
        var runId = Guid.NewGuid();
        var organizationId = Guid.NewGuid();
        var applicationId = Guid.NewGuid();
        var snapshot = SampleEnvironment();

        // test
        var row = snapshot.ToDynamoDbRow(runId, organizationId, applicationId);
        var roundTripped = row.ToRunEnvironmentSnapshot();

        // verify
        Assert.Equivalent(snapshot, roundTripped, strict: true);
        Assert.Equal(runId.ToString(), row["RunId"].S);
        Assert.Equal(organizationId.ToString(), row["OrganizationId"].S);
        Assert.Equal(applicationId.ToString(), row["ApplicationId"].S);
    }

    [Fact]
    public void EnvironmentRow_WhenRoundTripped_KeepsASensitiveVariableUnmasked()
    {
        // setup: nothing masks on the way into storage -- a Run is created holding real credentials, and
        // only Start Run overwrites them.
        var snapshot = SampleEnvironment();

        // test
        var row = snapshot.ToDynamoDbRow(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var roundTripped = row.ToRunEnvironmentSnapshot();

        // verify
        var sensitive = roundTripped.Variables.Single(v => v.IsSensitive);
        Assert.Equal("abc.................", sensitive.Value);
    }

    [Fact]
    public void EnvironmentRow_WhenMapped_UsesTheRunIdWithA2000SuffixAsItsRowKey()
    {
        // setup
        var runId = Guid.NewGuid();
        var snapshot = SampleEnvironment();

        // test
        var row = snapshot.ToDynamoDbRow(runId, Guid.NewGuid(), Guid.NewGuid());

        // verify
        Assert.Equal($"{runId}#2000", row["RowKey"].S);
        Assert.Equal(DynamoDbMapper.RunEnvironmentRowKey(runId), row["RowKey"].S);
    }

    [Fact]
    public void EnvironmentRow_WhenMapped_NeverCarriesHeaderId()
    {
        // setup: only header rows carry HeaderId -- that is what keeps the sparse RunHeaderIndex to one
        // entry per Run.
        var snapshot = SampleEnvironment();

        // test
        var row = snapshot.ToDynamoDbRow(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());

        // verify
        Assert.False(row.ContainsKey("HeaderId"));
    }

    [Fact]
    public void RunEnvironmentRowKey_SortsBetweenTheHeaderKeyAndTheStatusUpdatePrefix()
    {
        // setup: Get Run reads the header, the Environment row and every Scenario row with one BETWEEN
        // query bounded by the header key and the status-update prefix. That only works while the
        // Environment key sorts inside those bounds -- guaranteed here by the 1000/2000/4000 sort-key
        // prefixes, not by string comparison happening to agree with them.
        var runId = Guid.NewGuid();
        var headerKey = DynamoDbMapper.RunHeaderRowKey(runId);
        var environmentKey = DynamoDbMapper.RunEnvironmentRowKey(runId);
        var updatePrefix = DynamoDbMapper.RunStatusUpdateRowKeyPrefix(runId);

        // verify
        Assert.True(string.CompareOrdinal(headerKey, environmentKey) < 0);
        Assert.True(string.CompareOrdinal(environmentKey, updatePrefix) < 0);
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
        var row = snapshot.ToDynamoDbRow(runId, organizationId, applicationId);
        var roundTripped = row.ToRunScenarioSnapshot();

        // verify
        Assert.Equivalent(snapshot, roundTripped, strict: true);
        Assert.Equal(runId.ToString(), row["RunId"].S);
        Assert.Equal(organizationId.ToString(), row["OrganizationId"].S);
        Assert.Equal(applicationId.ToString(), row["ApplicationId"].S);
        Assert.Equal(snapshot.Source.Id.ToString(), row["ScenarioId"].S);
    }

    [Fact]
    public void ScenarioRow_WhenMapped_UsesTheRunAndScenarioIdWithA3000SuffixAsItsRowKey()
    {
        // setup
        var runId = Guid.NewGuid();
        var scenarioId = Guid.NewGuid();
        var snapshot = SampleScenarioSnapshot(scenarioId);

        // test
        var row = snapshot.ToDynamoDbRow(runId, Guid.NewGuid(), Guid.NewGuid());

        // verify
        Assert.Equal($"{runId}#3000#{scenarioId}", row["RowKey"].S);
        Assert.Equal(DynamoDbMapper.RunScenarioRowKey(runId, scenarioId), row["RowKey"].S);
    }

    [Fact]
    public void ScenarioRow_WhenMapped_NeverCarriesHeaderId()
    {
        // setup
        var snapshot = SampleScenarioSnapshot(Guid.NewGuid());

        // test
        var row = snapshot.ToDynamoDbRow(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());

        // verify: only a header row carries HeaderId -- see the sparse RunHeaderIndex.
        Assert.False(row.ContainsKey("HeaderId"));
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
        var row = update.ToDynamoDbRow(runId, organizationId, applicationId);
        var roundTripped = row.ToRunStatusUpdate();

        // verify
        Assert.Equivalent(update, roundTripped, strict: true);
        Assert.Equal(runId.ToString(), row["RunId"].S);
        Assert.Equal(organizationId.ToString(), row["OrganizationId"].S);
        Assert.Equal(applicationId.ToString(), row["ApplicationId"].S);
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
        var row = update.ToDynamoDbRow(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
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
        var row = update.ToDynamoDbRow(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());

        // verify
        Assert.False(row.ContainsKey("HeaderId"));
    }

    [Fact]
    public void StatusUpdateRow_WhenMapped_UsesTheRunIdAndPaddedSeqWithA4000SuffixAsItsRowKey()
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
        var row = update.ToDynamoDbRow(runId, Guid.NewGuid(), Guid.NewGuid());

        // verify
        Assert.Equal($"{runId}#4000#000000000009", row["RowKey"].S);
        Assert.Equal(DynamoDbMapper.RunStatusUpdateRowKey(runId, 9), row["RowKey"].S);
    }

    // ----- The padding function, and why it matters -----

    [Fact]
    public void RunStatusUpdateRowKey_WhenSequencesAreSortedAsStrings_SortsInNumericOrder()
    {
        // setup: 1, 9, 10 and 100 built into row keys for the same run. An unpadded scheme would sort
        // "#4000#10" before "#4000#9" as plain strings, silently reordering a poll cursor's results
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
        Assert.Equal($"{runId}#4000#000000000042", key);
    }
}
