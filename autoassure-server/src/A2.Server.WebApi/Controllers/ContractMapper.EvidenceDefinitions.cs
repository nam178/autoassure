using A2.Server.Engine.Contracts;
using EvidenceDefinition = A2.Server.Engine.Models.EvidenceDefinition;

namespace A2.Server.WebApi.Controllers;

/// <summary>Mapping between EvidenceDefinition Contracts and domain
/// Models.</summary>
public static partial class ContractMapper
{
    public static EvidenceDefinitionResponse ToResponse(
        this EvidenceDefinition evidence
    )
    {
        return new EvidenceDefinitionResponse(
            evidence.Id,
            evidence.Name,
            evidence.Description,
            evidence.ExampleValue
        );
    }
}
