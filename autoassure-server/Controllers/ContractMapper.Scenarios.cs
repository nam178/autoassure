using A2.Server.Contracts;
using Scenario = A2.Server.Models.Scenario;

namespace A2.Server.Controllers;

/// <summary>Mapping between Scenario Contracts and domain Models.</summary>
public static partial class ContractMapper
{
    public static ScenarioResponse ToResponse(this Scenario scenario) =>
        new(
            scenario.Id,
            scenario.Title,
            scenario.Description,
            scenario.Folder,
            scenario.Tags,
            scenario.Activities.Select(activity => activity.ToResponse()).ToList()
        );

    private static ActivityResponse ToResponse(this Models.Activity activity) =>
        new(activity.Id, activity.Description, activity.PreconditionIds, activity.EvidenceIds);
}
