using A2.Server.Models;
using A2.Server.Repositories;
using Environment = A2.Server.Models.Environment;

namespace A2.Server.Services;

public class RunSnapshotBuilder(
    IActivityRepository activityRepository,
    IPreconditionRepository preconditionRepository,
    IEvidenceDefinitionRepository evidenceDefinitionRepository,
    IEnvironmentVariableRepository environmentVariableRepository
) : IRunSnapshotBuilder
{
    public async Task<RunEnvironmentSnapshot> BuildEnvironmentSnapshotAsync(
        Guid organizationId,
        Environment environment
    )
    {
        var variables = await environmentVariableRepository.ListByEnvironmentAsync(
            organizationId,
            environment.Id
        );
        return RunEnvironmentSnapshot.FromEnvironment(
            environment,
            variables.Select(RunEnvironmentVariableSnapshot.FromEnvironmentVariable).ToList()
        );
    }

    public async Task<IReadOnlyList<RunScenarioSnapshot>> BuildScenarioSnapshotsAsync(
        Guid organizationId,
        Guid applicationId,
        IReadOnlyList<Scenario> scenarios
    )
    {
        // The Application's whole Precondition/EvidenceDefinition library is read once up front and
        // looked up by id per Activity below, rather than fetched per Activity -- Activities across many
        // Scenarios in the same Run typically reuse a handful of library rows, so this trades one extra
        // read for what would otherwise be dozens of repeated ones.
        var preconditionsById = (
            await preconditionRepository.ListByApplicationAsync(organizationId, applicationId)
        ).ToDictionary(p => p.Id);
        var evidenceDefinitionsById = (
            await evidenceDefinitionRepository.ListByApplicationAsync(organizationId, applicationId)
        ).ToDictionary(e => e.Id);

        var snapshots = new List<RunScenarioSnapshot>(scenarios.Count);
        foreach (var scenario in scenarios)
        {
            var activities = await activityRepository.ListByScenarioAsync(
                organizationId,
                scenario.Id
            );
            var activitySnapshots = activities
                .Select(activity =>
                    RunActivitySnapshot.FromActivity(
                        activity,
                        activity
                            .PreconditionIds.Where(preconditionsById.ContainsKey)
                            .Select(id =>
                                RunPreconditionSnapshot.FromPrecondition(preconditionsById[id])
                            )
                            .ToList(),
                        activity
                            .EvidenceIds.Where(evidenceDefinitionsById.ContainsKey)
                            .Select(id =>
                                RunEvidenceDefinitionSnapshot.FromEvidenceDefinition(
                                    evidenceDefinitionsById[id]
                                )
                            )
                            .ToList()
                    )
                )
                .ToList();
            snapshots.Add(RunScenarioSnapshot.FromScenario(scenario, activitySnapshots));
        }

        return snapshots;
    }
}
