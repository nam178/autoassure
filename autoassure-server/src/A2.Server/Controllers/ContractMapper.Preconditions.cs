using A2.Server.Contracts;
using ContractValueSource = A2.Server.Contracts.PreconditionValueSource;
using ModelValueSource = A2.Server.Models.PreconditionValueSource;
using Precondition = A2.Server.Models.Precondition;

namespace A2.Server.Controllers;

/// <summary>Mapping between Precondition Contracts and domain Models.</summary>
public static partial class ContractMapper
{
    public static PreconditionResponse ToResponse(this Precondition precondition) =>
        new(
            precondition.Id,
            precondition.Name,
            precondition.ValueSource.ToContract(),
            precondition.ExampleValue
        );

    public static ModelValueSource ToModel(this ContractValueSource valueSource) =>
        valueSource switch
        {
            ContractValueSource.PriorActivity => ModelValueSource.PriorActivity,
            ContractValueSource.AskAtRunTime => ModelValueSource.AskAtRunTime,
            ContractValueSource.SpecificValue => ModelValueSource.SpecificValue,
            _ => throw new ArgumentOutOfRangeException(nameof(valueSource)),
        };

    public static ContractValueSource ToContract(this ModelValueSource valueSource) =>
        valueSource switch
        {
            ModelValueSource.PriorActivity => ContractValueSource.PriorActivity,
            ModelValueSource.AskAtRunTime => ContractValueSource.AskAtRunTime,
            ModelValueSource.SpecificValue => ContractValueSource.SpecificValue,
            _ => throw new ArgumentOutOfRangeException(nameof(valueSource)),
        };
}
