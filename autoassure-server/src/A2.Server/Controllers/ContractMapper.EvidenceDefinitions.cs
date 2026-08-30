using A2.Server.Contracts;
using EvidenceDefinition = A2.Server.Models.EvidenceDefinition;

namespace A2.Server.Controllers;

/// <summary>Mapping between EvidenceDefinition Contracts and domain Models.</summary>
public static partial class ContractMapper
{
    public static EvidenceDefinitionResponse ToResponse(this EvidenceDefinition evidence) =>
        new(evidence.Id, evidence.Name, evidence.Description, evidence.ExampleValue);
}
