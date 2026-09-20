namespace A2.Server.Models;

/// <summary>
///     The lifecycle state of an entity. Shared across all first-class
///     entities in the system.
/// </summary>
public enum LifecycleState
{
    /// <summary>The entity is in active use.</summary>
    Active,

    /// <summary>The entity is archived and no longer in use.</summary>
    Archived,

    /// <summary>The entity is being deleted and will be purged soon.</summary>
    Deleting,
}
