using A2.Server.Engine.Contracts;
using ContractValueSource = A2.Server.Engine.Contracts.PreconditionValueSource;
using ModelValueSource = A2.Server.Engine.Models.PreconditionValueSource;
using Precondition = A2.Server.Engine.Models.Precondition;

namespace A2.Server.WebApi.Controllers;

/// <summary>Mapping between Precondition Contracts and domain Models.</summary>
public static partial class ContractMapper
{
    public static PreconditionResponse ToResponse(
        this Precondition precondition
    )
    {
        return new PreconditionResponse(
            precondition.Id,
            precondition.Name,
            precondition.ValueSource.ToContract(),
            precondition.ExampleValue
        );
    }

    public static ModelValueSource ToModel(this ContractValueSource valueSource)
    {
        return valueSource switch
        {
            ContractValueSource.PriorActivity => ModelValueSource.PriorActivity,
            ContractValueSource.AskAtRunTime => ModelValueSource.AskAtRunTime,
            ContractValueSource.SpecificValue => ModelValueSource.SpecificValue,
            _ => throw new ArgumentOutOfRangeException(nameof(valueSource)),
        };
    }

    public static ContractValueSource ToContract(
        this ModelValueSource valueSource
    )
    {
        return valueSource switch
        {
            ModelValueSource.PriorActivity => ContractValueSource.PriorActivity,
            ModelValueSource.AskAtRunTime => ContractValueSource.AskAtRunTime,
            ModelValueSource.SpecificValue => ContractValueSource.SpecificValue,
            _ => throw new ArgumentOutOfRangeException(nameof(valueSource)),
        };
    }
}
