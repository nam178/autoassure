namespace A2.Server.Common;

/// <summary>
///     App-related limits on first-class entities, enforced across controllers,
///     kept in one
///     place so they stay consistent and discoverable.
/// </summary>
public static class Quota
{
    public const int MaxActivityCountPerScenario = 90;
    public const int MaxScenariosPerRun = 96;

    public const int MaxActivityCountPerRun =
        MaxActivityCountPerScenario * MaxScenariosPerRun;
}
