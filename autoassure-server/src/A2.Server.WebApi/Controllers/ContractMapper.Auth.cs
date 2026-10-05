using A2.Server.UserManagement.Contracts;
using A2.Server.UserManagement.Models;

namespace A2.Server.WebApi.Controllers;

/// <summary>Mapping between Auth Contracts and domain Models.</summary>
public static partial class ContractMapper
{
    public static UserResponse ToResponse(this User user)
    {
        return new UserResponse(
            user.Id,
            user.FirstName,
            user.LastName,
            user.Email,
            user.EmailVerified
        );
    }
}
