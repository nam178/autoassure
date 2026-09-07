namespace A2.Server.Contracts;

/// <summary>What a Run status update row records, as returned to the client. AppendActivityResult is
/// the only kind that exists today -- see the server's design notes for what earns a new one.</summary>
public enum RunStatusUpdateKind
{
    AppendActivityResult,
}
