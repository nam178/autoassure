using A2.Server.UserManagement.Contracts;
using A2.Server.UserManagement.Models;

namespace A2.Server.WebApi.Controllers;

/// <summary>Mapping between Organization Contracts and domain Models.</summary>
public static partial class ContractMapper
{
    public static OrganizationResponse ToResponse(
        this Organization organization
    )
    {
        return new OrganizationResponse(
            organization.Id,
            organization.Name,
            organization.IsPersonal
        );
    }
}
