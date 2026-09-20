namespace A2.Server.Contracts;

/// <summary>Where a Precondition's value comes from at execution time.</summary>
public enum PreconditionValueSource
{
    PriorActivity,
    AskAtRunTime,
    SpecificValue,
}
