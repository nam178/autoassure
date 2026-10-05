using A2.Server.Engine.Contracts;
using Scenario = A2.Server.Engine.Models.Scenario;

namespace A2.Server.WebApi.Controllers;

/// <summary>Mapping between Scenario Contracts and domain Models.</summary>
public static partial class ContractMapper
{
    public static ScenarioResponse ToResponse(this Scenario scenario)
    {
        return new ScenarioResponse(
            scenario.Id,
            scenario.Title,
            scenario.Description,
            scenario.Folder,
            scenario.Tags
        );
    }
}
