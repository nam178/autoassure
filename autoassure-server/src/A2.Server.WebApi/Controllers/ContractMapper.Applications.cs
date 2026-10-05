using A2.Server.Engine.Contracts;
using A2.Server.Engine.Models;

namespace A2.Server.WebApi.Controllers;

/// <summary>Mapping between Application Contracts and domain Models.</summary>
public static partial class ContractMapper
{
    public static ApplicationResponse ToResponse(this Application application)
    {
        return new ApplicationResponse(
            application.Id,
            application.Name,
            application.Description
        );
    }
}
