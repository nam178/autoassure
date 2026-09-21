namespace A2.Server.Repositories;

public enum RemoveMembershipOutcome
{
    Removed,
    NotFound,
    CannotDeleteLastOwner,
}
