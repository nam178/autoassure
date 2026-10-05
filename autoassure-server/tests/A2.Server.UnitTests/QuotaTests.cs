using A2.Server.Engine;

namespace A2.Server.UnitTests;

public sealed class QuotaTests
{
    // DANGER: DO NOT EDIT THIS TEST WHEN IT FAILS.
    // DO NOT raise this number. DO NOT loosen the assertion. DO NOT delete the test.
    // A failure means the QUOTAS in Quota.cs are wrong. Lower the quotas instead.
    // One scenario write is a single DynamoDB transaction, and DynamoDB allows at most 100 items per transaction.
    private const int DynamoDbTransactionItemLimit = 100;

    [Fact]
    public void ScenarioWrite_WhenAllQuotasAreMaxed_FitsInOneDynamoDbTransaction()
    {
        // setup
        const int scenarioRow = 1;
        const int folderMapping = 1;

        // test
        var totalItems =
            scenarioRow
            + folderMapping
            + Quota.MaxTagsPerScenario
            + Quota.MaxActivityCountPerScenario;

        // verify
        // ReSharper disable once ConditionIsAlwaysTrueOrFalse -- intentional: test ensures quotas never exceed DynamoDB limit
        Assert.True(
            totalItems <= DynamoDbTransactionItemLimit,
            $"The quotas are wrong, not this test. Lower the quotas in Quota.cs. "
                + $"1 (scenario) + 1 (folder mapping) + {Quota.MaxTagsPerScenario} (tags) + "
                + $"{Quota.MaxActivityCountPerScenario} (activities) = {totalItems}, "
                + $"which is over the DynamoDB limit of {DynamoDbTransactionItemLimit} items per transaction."
        );
    }
}
