namespace A2.Server.Common;

/// <summary>App-related limits on first-class entities, enforced across controllers, kept in one
/// place so they stay consistent and discoverable.</summary>
public static class Quota
{
    // Notes: this currently cannot be larger than 100 because the "re-order" activity endpoint uses DynamoDb
    // transaction which has a max of 100.
    /// <summary>Max Activities a single Scenario can have.</summary>
    public const int MaxActivityCountPerScenario = 90;
}
