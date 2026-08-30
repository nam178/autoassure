using System.ComponentModel.DataAnnotations;

namespace A2.Server.Contracts;

/// <summary>Request body to edit an existing Scenario's Title/Description/Folder/Tags/Activities.</summary>
public record UpdateScenarioRequest(
    [Required, MaxLength(200)] string Title,
    [Required, MaxLength(10000)] string Description,
    [Required, MaxLength(300)] string Folder,
    IReadOnlyList<string>? Tags,
    IReadOnlyList<ActivityRequest>? Activities
);
