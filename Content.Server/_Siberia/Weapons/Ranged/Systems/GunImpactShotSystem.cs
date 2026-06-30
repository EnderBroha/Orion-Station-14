using Content.Server._Siberia.Weapons.Ranged.Components;
using Content.Server.Administration.Logs;
using Content.Server.Weapons.Ranged.Systems;
using Content.Shared.Database;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Popups;
using Content.Shared.Standing;
using Content.Shared.Throwing;
using Content.Shared.Weapons.Ranged;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Events;
using Content.Shared.Weapons.Ranged.Systems;
using Robust.Shared.Map;
using Robust.Shared.Random;

namespace Content.Server._Siberia.Weapons.Ranged.Systems;

/// <summary>
/// Handles accidental gun discharge when a firearm is thrown and hits something,
/// or when its holder is knocked down (e.g. slips) and drops it.
/// </summary>
public sealed class GunImpactShotSystem : EntitySystem
{
    [Dependency] private readonly IAdminLogManager _adminLogger = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly GunSystem _gun = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly EntityLookupSystem _lookup = default!;
    [Dependency] private readonly MobStateSystem _mobState = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<GunImpactShotComponent, ThrownEvent>(OnThrown);
        SubscribeLocalEvent<GunImpactShotComponent, ThrowDoHitEvent>(OnThrowDoHit);
        // Raised on the held gun when its holder is knocked down (e.g. slips) and drops it.
        SubscribeLocalEvent<GunImpactShotComponent, FellDownThrowAttemptEvent>(OnFellDownThrowAttempt);
    }

    private void OnThrown(EntityUid uid, GunImpactShotComponent component, ref ThrownEvent args)
    {
        // Re-arm for the new throw.
        component.Discharged = false;
    }

    private void OnThrowDoHit(EntityUid uid, GunImpactShotComponent component, ThrowDoHitEvent args)
    {
        if (args.Handled)
            return;

        // A throw collides with several hard fixtures (walls, floors, mobs), so
        // only let it discharge once per throw.
        if (component.Discharged)
            return;

        if (TryImpactShot((uid, component), args.Component.Thrower, allowTargeting: true))
            component.Discharged = true;
    }

    private void OnFellDownThrowAttempt(EntityUid uid, GunImpactShotComponent component, ref FellDownThrowAttemptEvent args)
    {
        // The holder was knocked down (e.g. slipped) and the gun is about to leave
        // their hands. It goes off while still in their grip: fired from their own
        // tile with no shooter exemption, the round spawns on them, so the classic
        // "fall over and shoot yourself" outcome is very much on the table.
        // Aim wildly rather than at a victim (the holder sits at zero distance).
        TryImpactShot((uid, component), args.Thrower, allowTargeting: false, popupId: "gun-impact-shot-dropped");
    }

    /// <summary>
    /// Rolls the discharge chance and, on success, fires a single accidental shot
    /// from the gun. Returns true only if a round was actually fired.
    /// </summary>
    /// <param name="shooter">Who is blamed for the shot in logs; null collision-wise so the round can hit anyone, including them.</param>
    /// <param name="allowTargeting">If false, always fire in a random direction instead of aiming at the nearest victim.</param>
    /// <param name="popupId">Localization id for the feedback popup shown at the gun.</param>
    public bool TryImpactShot(Entity<GunImpactShotComponent?> gun, EntityUid? shooter, bool allowTargeting = true, string popupId = "gun-impact-shot-fired")
    {
        if (!Resolve(gun, ref gun.Comp, false))
            return false;

        if (!TryComp<GunComponent>(gun, out var gunComp))
            return false;

        if (!_random.Prob(gun.Comp.Probability))
            return false;

        if (shooter != null && TerminatingOrDeleted(shooter.Value))
            shooter = null;

        var xform = Transform(gun);
        var coordinates = xform.Coordinates;

        var takeAmmo = new TakeAmmoEvent(1,
            new List<(EntityUid? Entity, IShootable Shootable)>(),
            coordinates,
            shooter);
        RaiseLocalEvent(gun.Owner, takeAmmo);

        if (takeAmmo.Ammo.Count == 0)
            return false;

        // Half the time it aims at the nearest living victim, otherwise it just
        // lets the round off in a random direction.
        EntityCoordinates toCoords;
        if (allowTargeting
            && _random.Prob(gun.Comp.TargetProbability)
            && TryFindVictim((gun.Owner, xform), gun.Comp.TargetRange, out var victim))
        {
            toCoords = Transform(victim).Coordinates;
        }
        else
        {
            toCoords = new EntityCoordinates(gun.Owner, _random.NextAngle().ToVec());
        }

        // user is left null on purpose: this is an accidental discharge with no one
        // aiming the gun, and passing a shooter would make the projectile ignore
        // them (ProjectileComponent.IgnoreShooter), so it could never hit the holder/thrower.
        _gun.Shoot(gun.Owner, gunComp, takeAmmo.Ammo, coordinates, toCoords, out _, user: null);

        // Raise GunShotEvent for pipeline consistency (sound, visuals, ammo counter, etc.).
        var shotEv = new GunShotEvent(shooter ?? gun.Owner, takeAmmo.Ammo);
        RaiseLocalEvent(gun.Owner, ref shotEv);

        _adminLogger.Add(LogType.Trigger, LogImpact.Medium,
            $"{ToPrettyString(gun.Owner):gun} accidentally fired (handled by {ToPrettyString(shooter):thrower})");

        _popup.PopupEntity(Loc.GetString(popupId, ("gun", gun.Owner)), gun.Owner);
        return true;
    }

    /// <summary>
    /// Picks a random living entity within range of the gun to aim the shot at.
    /// </summary>
    private bool TryFindVictim(Entity<TransformComponent> gun, float range, out EntityUid victim)
    {
        victim = default;

        var candidates = new List<EntityUid>();
        foreach (var entity in _lookup.GetEntitiesInRange<MobStateComponent>(
                     gun.Comp.Coordinates, range, LookupFlags.Dynamic))
        {
            if (entity.Owner == gun.Owner)
                continue;

            if (!_mobState.IsAlive(entity.Owner, entity.Comp))
                continue;

            candidates.Add(entity.Owner);
        }

        if (candidates.Count == 0)
            return false;

        victim = _random.Pick(candidates);
        return true;
    }
}
