namespace A2.Server.UserManagement.Repositories;

public enum RemoveMembershipOutcome
{
    Removed,
    NotFound,
    CannotDeleteLastOwner,
}
