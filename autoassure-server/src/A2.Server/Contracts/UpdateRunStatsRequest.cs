using System.ComponentModel.DataAnnotations;
using A2.Server.Common;

namespace A2.Server.Contracts;

/// <summary>Request body to overwrite a Running Run's four activity counts with absolute values -- never
/// an increment, so a retried call does no harm.</summary>
public record UpdateRunStatsRequest
{
    [Range(0, Quota.MaxActivityCountPerRun)]
    public required int TotalActivityCount { get; init; }

    [Range(0, Quota.MaxActivityCountPerRun)]
    public required int PassedActivityCount { get; init; }

    [Range(0, Quota.MaxActivityCountPerRun)]
    public required int FailedActivityCount { get; init; }

    [Range(0, Quota.MaxActivityCountPerRun)]
    public required int SkippedActivityCount { get; init; }
}
