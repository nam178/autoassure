using A2.Server.Contracts;
using A2.Server.Models;

namespace A2.Server.Controllers;

/// <summary>Mapping between Auth Contracts and domain Models.</summary>
public static partial class ContractMapper
{
    public static UserResponse ToResponse(this User user) =>
        new(user.Id, user.FirstName, user.LastName, user.Email, user.EmailVerified);
}
