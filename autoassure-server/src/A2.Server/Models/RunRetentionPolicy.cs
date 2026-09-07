namespace A2.Server.Models;

/// <summary>How long a Run's rows live before they are eligible to expire. An Authoring run -- created
/// by the Try button -- is a throwaway attempt and does not need to survive long; every other Run is a
/// durable record of what ran and is kept much longer. One flat quota per Trigger, kept in one place so
/// it can become a per-organization setting later without hunting through every query that reads a
/// retention number.</summary>
public static class RunRetentionPolicy
{
    /// <summary>How long an Authoring Run's rows live.</summary>
    public static readonly TimeSpan AuthoringRetention = TimeSpan.FromDays(7);

    /// <summary>How long every other Run's rows live.</summary>
    public static readonly TimeSpan DefaultRetention = TimeSpan.FromDays(3 * 365);

    /// <summary>The DynamoDB TTL, epoch seconds, that every row of a Run created now with this Trigger
    /// must carry -- the header, every Scenario snapshot, and every status update row. Every row of one
    /// Run MUST carry the exact same value, so they all disappear together once DynamoDB's TTL sweep
    /// runs.</summary>
    public static long ExpiresAt(RunTrigger trigger, DateTimeOffset createdAt) =>
        (
            createdAt + (trigger == RunTrigger.Authoring ? AuthoringRetention : DefaultRetention)
        ).ToUnixTimeSeconds();
}
