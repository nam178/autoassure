using A2.Server.Engine.Contracts;
using Activity = A2.Server.Engine.Models.Activity;

namespace A2.Server.WebApi.Controllers;

/// <summary>Mapping between Activity Contracts and domain Models.</summary>
public static partial class ContractMapper
{
    public static ActivityResponse ToResponse(this Activity activity)
    {
        return new ActivityResponse
        {
            Id = activity.Id,
            ScenarioId = activity.ScenarioId,
            Description = activity.Description,
            Order = activity.Order,
            PreconditionIds = activity.PreconditionIds,
            EvidenceIds = activity.EvidenceIds,
            CreatedByUserId = activity.CreatedByUserId,
            UpdatedByUserId = activity.UpdatedByUserId,
            CreatedAt = activity.CreatedAt,
            UpdatedAt = activity.UpdatedAt,
        };
    }
}
