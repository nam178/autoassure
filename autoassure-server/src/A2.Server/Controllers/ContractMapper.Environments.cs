using A2.Server.Contracts;
using A2.Server.Models;
using ContractEnvironmentClassification = A2.Server.Contracts.EnvironmentClassification;
using Environment = A2.Server.Models.Environment;
using ModelEnvironmentClassification = A2.Server.Models.EnvironmentClassification;

namespace A2.Server.Controllers;

/// <summary>Mapping between Environment Contracts and domain Models.</summary>
public static partial class ContractMapper
{
    // The API keeps more of a sensitive value visible than a run snapshot does, since the API answers
    // "did I paste the right key?" live, while a snapshot lives on for years.
    private const double SensitiveVariableFractionToKeep = 0.3;


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

    /// <summary>Maps a single Environment variable to its response representation. A sensitive
    /// variable's Value is masked, keeping only its first 30% of characters.</summary>
    private static EnvironmentVariableResponse ToResponse(this EnvironmentVariable variable) =>
        new(
            variable.Key,
            variable.IsSensitive
                ? SensitiveValueMasker.Mask(variable.Value, SensitiveVariableFractionToKeep)
                : variable.Value,
            variable.IsSensitive
        );

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

    private static ContractEnvironmentClassification ToContract(
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
