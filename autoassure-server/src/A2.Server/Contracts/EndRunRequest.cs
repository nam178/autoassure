using System.ComponentModel.DataAnnotations;

namespace A2.Server.Contracts;

/// <summary>Request body to end a Running Run. TerminalStatus must be Completed, Cancelled or Abandoned --
/// Pending and Running are rejected, since those are states the server itself moves a Run through, never
/// an outcome a caller declares. StatusReason may only be given alongside Abandoned; Completed and
/// Cancelled are self-explanatory and must leave it null.</summary>
public record EndRunRequest
{
    [EnumDataType(typeof(RunStatus))]
    public required RunStatus TerminalStatus { get; init; }

    [EnumDataType(typeof(RunStatusReason))]
    public RunStatusReason? StatusReason { get; init; }
}
