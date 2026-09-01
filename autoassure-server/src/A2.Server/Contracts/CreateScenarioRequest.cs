using System.ComponentModel.DataAnnotations;

namespace A2.Server.Contracts;

/// <summary>Request body to create a new Scenario for an Application. Folder defaults to "/" when
/// not given; Tags default to empty.</summary>
public record CreateScenarioRequest(
    [Required, MaxLength(200)] string Title,
    [Required, MaxLength(10000)] string Description,
    [MaxLength(300)] string? Folder,
    [MaxLength(20)] IReadOnlyList<string>? Tags,
    [MaxLength(200)] IReadOnlyList<ActivityRequest>? Activities
);
