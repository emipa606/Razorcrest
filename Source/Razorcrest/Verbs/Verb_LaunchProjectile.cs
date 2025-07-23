using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace Razorcrest;

public class Verb_LaunchProjectile : Verb
{
    protected virtual ThingDef Projectile
    {
        get
        {
            if (EquipmentSource == null)
            {
                return verbProps.defaultProjectile;
            }

            var comp = EquipmentSource.GetComp<CompChangeableProjectile>();
            return comp is { Loaded: true } ? comp.Projectile : verbProps.defaultProjectile;
        }
    }

    public override void WarmupComplete()
    {
        base.WarmupComplete();
        Find.BattleLog.Add(new BattleLogEntry_RangedFire(caster, currentTarget.HasThing ? currentTarget.Thing : null,
            EquipmentSource?.def, Projectile, ShotsPerBurst > 1));
    }

    protected override bool TryCastShot()
    {
        if (currentTarget.HasThing && currentTarget.Thing.Map != caster.Map)
        {
            return false;
        }

        var projectile = Projectile;
        if (projectile == null)
        {
            return false;
        }

        var shootLineFromTo = TryFindShootLineFromTo(caster.Position, currentTarget, out var resultingLine);
        if (verbProps.stopBurstWithoutLos && !shootLineFromTo)
        {
            return false;
        }

        if (EquipmentSource != null)
        {
            EquipmentSource.GetComp<CompChangeableProjectile>()?.Notify_ProjectileLaunched();
            EquipmentSource.GetComp<CompApparelReloadable>()?.UsedOnce();
        }

        var launcher = caster;
        Thing equipment = EquipmentSource;
        var compMannable = caster.TryGetComp<CompMannable>();
        if (compMannable is { ManningPawn: not null })
        {
            launcher = compMannable.ManningPawn;
            equipment = caster;
        }

        var drawPos = caster.DrawPos;
        var drawOffset = caster.def.GetModExtension<TurretPosOffset>();
        if (drawOffset != null)
        {
            drawPos += Quaternion.Euler(0, caster.Rotation.AsAngle, 0) * drawOffset.posOffset;
        }

        var cells = resultingLine.Points().Where(x => !caster.OccupiedRect().Cells.Contains(x));
        var distance = cells.First().DistanceTo(drawPos.ToIntVec3());
        if (distance > 2f)
        {
            return false;
        }

        var projectile2 = (Projectile)GenSpawn.Spawn(projectile, resultingLine.Source, caster.Map);
        ProjectileHitFlags hitFlags;
        if (verbProps.ForcedMissRadius > 0.5f)
        {
            var num = VerbUtility.CalculateAdjustedForcedMiss(verbProps.ForcedMissRadius,
                currentTarget.Cell - caster.Position);
            if (num > 0.5f)
            {
                var max = GenRadial.NumCellsInRadius(num);
                var num2 = Rand.Range(0, max);
                if (num2 > 0)
                {
                    var c = currentTarget.Cell + GenRadial.RadialPattern[num2];
                    throwDebugText("ToRadius");
                    throwDebugText("Rad\nDest", c);
                    hitFlags = ProjectileHitFlags.NonTargetWorld;
                    if (Rand.Chance(0.5f))
                    {
                        hitFlags = ProjectileHitFlags.All;
                    }

                    if (!canHitNonTargetPawnsNow)
                    {
                        hitFlags &= ~ProjectileHitFlags.NonTargetPawns;
                    }

                    projectile2.Launch(launcher, drawPos, c, currentTarget, hitFlags, equipment: equipment);
                    return true;
                }
            }
        }

        var shotReport = ShotReport.HitReportFor(caster, this, currentTarget);
        var randomCoverToMissInto = shotReport.GetRandomCoverToMissInto();
        var targetCoverDef = randomCoverToMissInto?.def;
        if (!Rand.Chance(shotReport.AimOnTargetChance_IgnoringPosture))
        {
            resultingLine.ChangeDestToMissWild(shotReport.AimOnTargetChance_StandardTarget, false, caster.Map);
            throwDebugText($"ToWild{(canHitNonTargetPawnsNow ? "\nchntp" : "")}");
            throwDebugText("Wild\nDest", resultingLine.Dest);
            hitFlags = ProjectileHitFlags.NonTargetWorld;
            if (Rand.Chance(0.5f) && canHitNonTargetPawnsNow)
            {
                hitFlags |= ProjectileHitFlags.NonTargetPawns;
            }

            projectile2.Launch(launcher, drawPos, resultingLine.Dest, currentTarget, hitFlags, false,
                equipment, targetCoverDef);
            return true;
        }

        if (currentTarget.Thing != null && currentTarget.Thing.def.category == ThingCategory.Pawn &&
            !Rand.Chance(shotReport.PassCoverChance))
        {
            throwDebugText($"ToCover{(canHitNonTargetPawnsNow ? "\nchntp" : "")}");
            if (randomCoverToMissInto == null)
            {
                return true;
            }

            throwDebugText("Cover\nDest", randomCoverToMissInto.Position);
            hitFlags = ProjectileHitFlags.NonTargetWorld;
            if (canHitNonTargetPawnsNow)
            {
                hitFlags |= ProjectileHitFlags.NonTargetPawns;
            }

            projectile2.Launch(launcher, drawPos, randomCoverToMissInto, currentTarget, hitFlags, false,
                equipment, targetCoverDef);

            return true;
        }

        hitFlags = ProjectileHitFlags.IntendedTarget;
        if (canHitNonTargetPawnsNow)
        {
            hitFlags |= ProjectileHitFlags.NonTargetPawns;
        }

        if (currentTarget.Thing != null &&
            (!currentTarget.HasThing || currentTarget.Thing.def.Fillage == FillCategory.Full))
        {
            hitFlags |= ProjectileHitFlags.NonTargetWorld;
        }

        throwDebugText($"ToHit{(canHitNonTargetPawnsNow ? "\nchntp" : "")}");
        if (currentTarget.Thing != null)
        {
            projectile2.Launch(launcher, drawPos, currentTarget, currentTarget, hitFlags, false, equipment,
                targetCoverDef);
            throwDebugText("Hit\nDest", currentTarget.Cell);
        }
        else
        {
            projectile2.Launch(launcher, drawPos, resultingLine.Dest, currentTarget, hitFlags, false,
                equipment, targetCoverDef);
            throwDebugText("Hit\nDest", resultingLine.Dest);
        }

        return true;
    }

    private void throwDebugText(string text)
    {
        if (DebugViewSettings.drawShooting)
        {
            MoteMaker.ThrowText(caster.DrawPos, caster.Map, text);
        }
    }

    private void throwDebugText(string text, IntVec3 c)
    {
        if (DebugViewSettings.drawShooting)
        {
            MoteMaker.ThrowText(c.ToVector3Shifted(), caster.Map, text);
        }
    }

    public override float HighlightFieldRadiusAroundTarget(out bool needLOSToCenter)
    {
        needLOSToCenter = true;
        return Projectile?.projectile.explosionRadius ?? 0f;
    }

    public override bool Available()
    {
        if (!base.Available())
        {
            return false;
        }

        if (!CasterIsPawn)
        {
            return Projectile != null;
        }

        var casterPawn = CasterPawn;
        if (casterPawn.Faction != Faction.OfPlayer && casterPawn.mindState.MeleeThreatStillThreat &&
            casterPawn.mindState.meleeThreat.Position.AdjacentTo8WayOrInside(casterPawn.Position))
        {
            return false;
        }

        return Projectile != null;
    }
}