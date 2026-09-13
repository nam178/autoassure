using A2.Server.Models;
using Environment = A2.Server.Models.Environment;

namespace A2.Server.Services;

/// <summary>Assembles the snapshot rows a Run is created with, by reading the live Scenarios, their
/// Activities, the Preconditions/EvidenceDefinitions those Activities reference, and an Environment's
/// variables, then copying each into its dedicated snapshot type (see the snapshot models in
/// <c>Models/</c>).</summary>
public interface IRunSnapshotBuilder
{
    /// <summary>Copies <paramref name="environment"/> and its current variables into a
    /// <see cref="RunEnvironmentSnapshot"/>, value included whole even for a sensitive variable -- a
    /// future execution agent needs the real credential from this snapshot to connect to systems under
    /// test. Nothing is masked here: masking happens later, when the Run is claimed (see
    /// <c>IRunRepository.TryMarkAsStartedAsync</c>) and at the response-mapping boundary (see
    /// <c>ContractMapper.ToResponse(Run, bool)</c>).</summary>
    Task<RunEnvironmentSnapshot> BuildEnvironmentSnapshotAsync(
        Guid organizationId,
        Environment environment
    );

    /// <summary>Copies each of <paramref name="scenarios"/> -- and, for each, its Activities and the
    /// Preconditions/EvidenceDefinitions they currently reference -- into one
    /// <see cref="RunScenarioSnapshot"/> per Scenario, in the same order as <paramref name="scenarios"/>.
    /// A Precondition or EvidenceDefinition id an Activity references but that no longer resolves is left
    /// out of that Activity's snapshot rather than failing the whole Run: today's repositories hard-delete
    /// library rows without detaching Activities that reference them, so a stale id already means "this
    /// reference is gone" everywhere else in the app too.
    ///
    /// <paramref name="scenarios"/> MUST all belong to <paramref name="applicationId"/> -- the caller is
    /// responsible for that check; this method does not repeat it.</summary>
    Task<IReadOnlyList<RunScenarioSnapshot>> BuildScenarioSnapshotsAsync(
        Guid organizationId,
        Guid applicationId,
        IReadOnlyList<Scenario> scenarios
    );
}
