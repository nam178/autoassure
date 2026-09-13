namespace A2.Server.Models;

/// <summary>How long a Run stays queryable before it expires. An Authoring run -- created by the Try
/// button -- is a throwaway attempt and does not need to survive long; every other Run is a durable
/// record of what ran and is kept much longer. One flat quota per Trigger, kept in one place so it can
/// become a per-organization setting later without hunting through every query that reads a retention
/// number.</summary>
public static class RunRetentionPolicy
{
    /// <summary>How long an Authoring Run stays queryable.</summary>
    public static readonly TimeSpan AuthoringRetention = TimeSpan.FromDays(7);

    /// <summary>How long every other Run stays queryable.</summary>
    public static readonly TimeSpan DefaultRetention = TimeSpan.FromDays(3 * 365);

    /// <summary>The point in time after which a Run created now with this Trigger stops being retained
    /// and can no longer be found. Every part of the Run -- its identity, what it ran, and its status
    /// update log -- shares this exact value, computed once from when the Run was created, so they all
    /// expire together.</summary>
    public static DateTimeOffset ExpiresAt(RunTrigger trigger, DateTimeOffset createdAt) =>
        createdAt + (trigger == RunTrigger.Authoring ? AuthoringRetention : DefaultRetention);
}
