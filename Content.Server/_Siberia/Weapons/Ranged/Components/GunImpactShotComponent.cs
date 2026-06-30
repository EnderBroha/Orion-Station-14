using Robust.Shared.GameObjects;

namespace Content.Server._Siberia.Weapons.Ranged.Components;

/// <summary>
/// When a gun with this component is thrown and hits an entity,
/// there is a chance it will accidentally fire in a random direction.
/// </summary>
[RegisterComponent]
public sealed partial class GunImpactShotComponent : Component
{
    /// <summary>
    /// Probability (0..1) that the gun fires on impact.
    /// </summary>
    [DataField]
    public float Probability = 0.5f;

    /// <summary>
    /// Probability (0..1) that the shot is aimed at the nearest living entity
    /// instead of firing in a random direction.
    /// </summary>
    [DataField]
    public float TargetProbability = 0.25f;

    /// <summary>
    /// Radius (in metres) to look for a victim to aim at.
    /// </summary>
    [DataField]
    public float TargetRange = 6f;

    /// <summary>
    /// Set true once the gun has discharged during the current throw, so a single
    /// throw can only go off once even when it collides with several things.
    /// Reset when the gun is thrown again.
    /// </summary>
    [ViewVariables]
    public bool Discharged;
}
