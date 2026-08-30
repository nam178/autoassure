using A2.Server.Contracts;
using A2.Server.Models;

namespace A2.Server.Controllers;

/// <summary>Mapping between Application Contracts and domain Models.</summary>
public static partial class ContractMapper
{
    public static ApplicationResponse ToResponse(this Application application) =>
        new(application.Id, application.Name, application.Description);
}
