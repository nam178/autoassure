using A2.Server.Contracts;
using A2.Server.Models;
using ContractEnvironmentClassification = A2.Server.Contracts.EnvironmentClassification;
using Environment = A2.Server.Models.Environment;
using ModelEnvironmentClassification = A2.Server.Models.EnvironmentClassification;

namespace A2.Server.Controllers;

/// <summary>Mapping between Environment Contracts and domain Models.</summary>
public static partial class ContractMapper
{
    /// <summary>Combines an Environment with its Variables into the response returned to the client.</summary>
    public static EnvironmentResponse ToResponse(
        this Environment environment,
        IReadOnlyList<EnvironmentVariable> variables
    ) =>
        new(
            environment.Id,
            environment.Name,
            environment.Classification.ToContract(),
            variables.Select(v => v.ToResponse()).ToList()
        );

    /// <summary>Maps a single Environment variable to its response representation.</summary>
    public static EnvironmentVariableResponse ToResponse(this EnvironmentVariable variable) =>
        new(variable.Key, variable.Value);

    public static ModelEnvironmentClassification ToModel(
        this ContractEnvironmentClassification classification
    ) =>
        classification switch
        {
            ContractEnvironmentClassification.Production =>
                ModelEnvironmentClassification.Production,
            ContractEnvironmentClassification.NonProduction =>
                ModelEnvironmentClassification.NonProduction,
            _ => throw new ArgumentOutOfRangeException(nameof(classification)),
        };

    public static ContractEnvironmentClassification ToContract(
        this ModelEnvironmentClassification classification
    ) =>
        classification switch
        {
            ModelEnvironmentClassification.Production =>
                ContractEnvironmentClassification.Production,
            ModelEnvironmentClassification.NonProduction =>
                ContractEnvironmentClassification.NonProduction,
            _ => throw new ArgumentOutOfRangeException(nameof(classification)),
        };
}
