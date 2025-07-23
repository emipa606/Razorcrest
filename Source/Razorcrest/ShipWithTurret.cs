using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.Sound;

namespace Razorcrest;

[StaticConstructorOnStartup]
public class ShipWithTurret : Building, IAttackTarget, IAttackTargetSearcher
{
    private const int TryStartShootSomethingIntervalTicks = 10;

    private const float SightRadiusTurret = 13.4f;

    private static readonly Material ForcedTargetLineMat =
        MaterialPool.MatFrom(GenDraw.LineTexPath, ShaderDatabase.Transparent, new Color(1f, 0.5f, 0.5f));

    private int burstCooldownTicksLeft;

    private int burstWarmupTicksLeft;

    private LocalTargetInfo currentTargetInt = LocalTargetInfo.Invalid;

    private CompCanBeDormant dormantComp;

    private LocalTargetInfo forcedTarget = LocalTargetInfo.Invalid;

    private Thing gun;

    private bool holdFire;

    private CompInitiatable initiatableComp;

    private LocalTargetInfo lastAttackedTarget;

    private int lastAttackTargetTick;

    private CompMannable mannableComp;

    private CompPowerTrader powerComp;

    private Effecter progressBarEffecter;

    private StunHandler stunner;

    public ShipWithTurret()
    {
        stunner = new StunHandler(this);
    }

    private bool Active
    {
        get
        {
            if (powerComp is { PowerOn: false } || dormantComp is { Awake: false })
            {
                return false;
            }

            return initiatableComp == null || initiatableComp.Initiated;
        }
    }

    private bool PlayerControlled
    {
        get
        {
            if (Faction == Faction.OfPlayer || MannedByColonist)
            {
                return !MannedByNonColonist;
            }

            return false;
        }
    }

    private bool CanSetForcedTarget => mannableComp != null && PlayerControlled;

    private bool CanToggleHoldFire => PlayerControlled;

    private bool IsMortar => def.building.IsMortar;

    private bool IsMortarOrProjectileFliesOverhead => AttackVerb.ProjectileFliesOverhead() || IsMortar;

    private bool CanExtractShell
    {
        get
        {
            if (!PlayerControlled)
            {
                return false;
            }

            return gun.TryGetComp<CompChangeableProjectile>()?.Loaded ?? false;
        }
    }

    private bool MannedByColonist
    {
        get
        {
            if (mannableComp is { ManningPawn: not null })
            {
                return mannableComp.ManningPawn.Faction == Faction.OfPlayer;
            }

            return false;
        }
    }

    private bool MannedByNonColonist
    {
        get
        {
            if (mannableComp is { ManningPawn: not null })
            {
                return mannableComp.ManningPawn.Faction != Faction.OfPlayer;
            }

            return false;
        }
    }

    private CompEquippable GunCompEq => gun.TryGetComp<CompEquippable>();

    private LocalTargetInfo CurrentTarget => currentTargetInt;

    private bool WarmingUp => burstWarmupTicksLeft > 0;

    private Verb AttackVerb => GunCompEq.PrimaryVerb;

    public bool IsMannable => mannableComp != null;

    Thing IAttackTarget.Thing => this;

    public LocalTargetInfo TargetCurrentlyAimingAt => CurrentTarget;

    public float TargetPriorityFactor => 1f;

    public bool ThreatDisabled(IAttackTargetSearcher disabledFor)
    {
        var comp = GetComp<CompPowerTrader>();
        if (comp is { PowerOn: false })
        {
            return true;
        }

        var comp2 = GetComp<CompMannable>();
        if (comp2 is { MannedNow: false })
        {
            return true;
        }

        var comp3 = GetComp<CompCanBeDormant>();
        if (comp3 is { Awake: false })
        {
            return true;
        }

        var comp4 = GetComp<CompInitiatable>();
        return comp4 is { Initiated: false };
    }

    Thing IAttackTargetSearcher.Thing => this;

    public Verb CurrentEffectiveVerb => AttackVerb;

    public LocalTargetInfo LastAttackedTarget => lastAttackedTarget;

    public int LastAttackTargetTick => lastAttackTargetTick;

    public override void SpawnSetup(Map map, bool respawningAfterLoad)
    {
        base.SpawnSetup(map, respawningAfterLoad);
        dormantComp = GetComp<CompCanBeDormant>();
        initiatableComp = GetComp<CompInitiatable>();
        powerComp = GetComp<CompPowerTrader>();
        mannableComp = GetComp<CompMannable>();
        if (!respawningAfterLoad)
        {
            burstCooldownTicksLeft = def.building.turretInitialCooldownTime.SecondsToTicks();
        }

        updateGunVerbs();
    }

    public override void PostMake()
    {
        base.PostMake();
        makeGun();
    }

    public override void DeSpawn(DestroyMode mode = DestroyMode.Vanish)
    {
        base.DeSpawn(mode);
        resetCurrentTarget();
        progressBarEffecter?.Cleanup();
    }

    public override void ExposeData()
    {
        base.ExposeData();
        Scribe_TargetInfo.Look(ref forcedTarget, "forcedTarget");
        Scribe_TargetInfo.Look(ref lastAttackedTarget, "lastAttackedTarget");
        Scribe_Deep.Look(ref stunner, "stunner", this);
        Scribe_Values.Look(ref lastAttackTargetTick, "lastAttackTargetTick");

        Scribe_Values.Look(ref burstCooldownTicksLeft, "burstCooldownTicksLeft");
        Scribe_Values.Look(ref burstWarmupTicksLeft, "burstWarmupTicksLeft");
        Scribe_TargetInfo.Look(ref currentTargetInt, "currentTarget");
        Scribe_Values.Look(ref holdFire, "holdFire");
        Scribe_Deep.Look(ref gun, "gun");
        BackCompatibility.PostExposeData(this);
        if (Scribe.mode == LoadSaveMode.PostLoadInit)
        {
            updateGunVerbs();
        }
    }

    public override AcceptanceReport ClaimableBy(Faction by)
    {
        if (!base.ClaimableBy(by))
        {
            return false;
        }

        if (mannableComp is { ManningPawn: not null })
        {
            return false;
        }

        if (Active && mannableComp == null)
        {
            return false;
        }

        return dormantComp is not { Awake: false } && initiatableComp is not { Initiated: false } ||
               powerComp is { PowerOn: false };
    }

    public void OrderAttack(LocalTargetInfo targ)
    {
        if (!targ.IsValid)
        {
            if (forcedTarget.IsValid)
            {
                resetForcedTarget();
            }

            return;
        }

        if ((targ.Cell - Position).LengthHorizontal < AttackVerb.verbProps.EffectiveMinRange(targ, this))
        {
            Messages.Message("MessageTargetBelowMinimumRange".Translate(), this, MessageTypeDefOf.RejectInput, false);
            return;
        }

        if ((targ.Cell - Position).LengthHorizontal > AttackVerb.verbProps.range)
        {
            Messages.Message("MessageTargetBeyondMaximumRange".Translate(), this, MessageTypeDefOf.RejectInput, false);
            return;
        }

        if (forcedTarget != targ)
        {
            forcedTarget = targ;
            if (burstCooldownTicksLeft <= 0)
            {
                tryStartShootSomething(false);
            }
        }

        if (holdFire)
        {
            Messages.Message("MessageTurretWontFireBecauseHoldFire".Translate(def.label), this,
                MessageTypeDefOf.RejectInput, false);
        }
    }

    protected override void Tick()
    {
        base.Tick();
        if (forcedTarget.HasThing && (!forcedTarget.Thing.Spawned || !Spawned || forcedTarget.Thing.Map != Map))
        {
            forcedTarget = LocalTargetInfo.Invalid;
        }

        stunner.StunHandlerTick();

        if (CanExtractShell && MannedByColonist)
        {
            var compChangeableProjectile = gun.TryGetComp<CompChangeableProjectile>();
            if (!compChangeableProjectile.allowedShellsSettings.AllowedToAccept(compChangeableProjectile.LoadedShell))
            {
                extractShell();
            }
        }

        if (forcedTarget.IsValid && !CanSetForcedTarget)
        {
            resetForcedTarget();
        }

        if (!CanToggleHoldFire)
        {
            holdFire = false;
        }

        if (forcedTarget.ThingDestroyed)
        {
            resetForcedTarget();
        }

        if (Active && (mannableComp == null || mannableComp.MannedNow) && !stunner.Stunned && Spawned)
        {
            GunCompEq.verbTracker.VerbsTick();
            if (AttackVerb.state == VerbState.Bursting)
            {
                return;
            }

            if (WarmingUp)
            {
                burstWarmupTicksLeft--;
                if (burstWarmupTicksLeft == 0)
                {
                    beginBurst();
                }
            }
            else
            {
                if (burstCooldownTicksLeft > 0)
                {
                    burstCooldownTicksLeft--;
                    if (IsMortar)
                    {
                        progressBarEffecter ??= EffecterDefOf.ProgressBar.Spawn();

                        progressBarEffecter.EffectTick(this, TargetInfo.Invalid);
                        var mote = ((SubEffecter_ProgressBar)progressBarEffecter.children[0]).mote;
                        mote.progress = 1f - (Math.Max(burstCooldownTicksLeft, 0) /
                                              (float)burstCooldownTime().SecondsToTicks());
                        mote.offsetZ = -0.8f;
                    }
                }

                if (burstCooldownTicksLeft <= 0 && this.IsHashIntervalTick(10))
                {
                    tryStartShootSomething(true);
                }
            }
        }
        else
        {
            resetCurrentTarget();
        }
    }

    private void tryStartShootSomething(bool canBeginBurstImmediately)
    {
        if (progressBarEffecter != null)
        {
            progressBarEffecter.Cleanup();
            progressBarEffecter = null;
        }

        if (!Spawned || holdFire && CanToggleHoldFire ||
            AttackVerb.ProjectileFliesOverhead() && Map.roofGrid.Roofed(Position) || !AttackVerb.Available())
        {
            resetCurrentTarget();
            return;
        }

        var isValid = currentTargetInt.IsValid;
        currentTargetInt = forcedTarget.IsValid ? forcedTarget : tryFindNewTarget();

        if (!isValid && currentTargetInt.IsValid)
        {
            SoundDefOf.TurretAcquireTarget.PlayOneShot(new TargetInfo(Position, Map));
        }

        if (currentTargetInt.IsValid)
        {
            var randomInRange = def.building.turretBurstWarmupTime.RandomInRange;
            if (randomInRange > 0f)
            {
                burstWarmupTicksLeft = randomInRange.SecondsToTicks();
            }
            else if (canBeginBurstImmediately)
            {
                beginBurst();
            }
            else
            {
                burstWarmupTicksLeft = 1;
            }
        }
        else
        {
            resetCurrentTarget();
        }
    }

    private LocalTargetInfo tryFindNewTarget()
    {
        var attackTargetSearcher = targSearcher();
        var faction = attackTargetSearcher.Thing.Faction;
        var range = AttackVerb.verbProps.range;
        if (Rand.Value < 0.5f && AttackVerb.ProjectileFliesOverhead() && faction.HostileTo(Faction.OfPlayer) && Map
                .listerBuildings.allBuildingsColonist.Where(delegate(Building x)
                {
                    var num = AttackVerb.verbProps.EffectiveMinRange(x, this);
                    float num2 = x.Position.DistanceToSquared(Position);
                    return num2 > num * num && num2 < range * range;
                }).TryRandomElement(out var result))
        {
            return result;
        }

        var targetScanFlags = TargetScanFlags.NeedThreat | TargetScanFlags.NeedAutoTargetable;
        if (!AttackVerb.ProjectileFliesOverhead())
        {
            targetScanFlags |= TargetScanFlags.NeedLOSToAll;
            targetScanFlags |= TargetScanFlags.LOSBlockableByGas;
        }

        if (AttackVerb.IsIncendiary_Ranged())
        {
            targetScanFlags |= TargetScanFlags.NeedNonBurning;
        }

        if (IsMortar)
        {
            targetScanFlags |= TargetScanFlags.NeedNotUnderThickRoof;
        }

        return (Thing)AttackTargetFinder.BestShootTargetFromCurrentPosition(attackTargetSearcher, targetScanFlags,
            isValidTarget);
    }

    private IAttackTargetSearcher targSearcher()
    {
        if (mannableComp is { MannedNow: true })
        {
            return mannableComp.ManningPawn;
        }

        return this;
    }

    private bool isValidTarget(Thing t)
    {
        var drawPos = DrawPos;
        var drawOffset = def.GetModExtension<TurretPosOffset>();
        if (drawOffset != null)
        {
            drawPos += Quaternion.Euler(0, Rotation.AsAngle, 0) * drawOffset.posOffset;
        }

        if (AttackVerb.TryFindShootLineFromTo(drawPos.ToIntVec3(), t, out var resultingLine))
        {
            var cells = resultingLine.Points().Where(x => !this.OccupiedRect().Cells.Contains(x));
            var distance = cells.First().DistanceTo(drawPos.ToIntVec3());
            if (distance > 2f)
            {
                return false;
            }
        }
        else
        {
            Log.Message($"{AttackVerb} - Cant find a shoot line");
            return false;
        }

        if (t is not Pawn pawn)
        {
            return true;
        }

        if (AttackVerb.ProjectileFliesOverhead())
        {
            var roofDef = Map.roofGrid.RoofAt(t.Position);
            if (roofDef is { isThickRoof: true })
            {
                return false;
            }
        }

        if (mannableComp == null)
        {
            return !GenAI.MachinesLike(Faction, pawn);
        }

        return !pawn.RaceProps.Animal || pawn.Faction != Faction.OfPlayer;
    }

    private void beginBurst()
    {
        AttackVerb.TryStartCastOn(CurrentTarget);
        OnAttackedTarget(CurrentTarget);
    }

    private void burstComplete()
    {
        burstCooldownTicksLeft = burstCooldownTime().SecondsToTicks();
    }

    private float burstCooldownTime()
    {
        return def.building.turretBurstCooldownTime >= 0f
            ? def.building.turretBurstCooldownTime
            : AttackVerb.verbProps.defaultCooldownTime;
    }

    public override string GetInspectString()
    {
        var stringBuilder = new StringBuilder();
        var inspectString = base.GetInspectString();
        if (!inspectString.NullOrEmpty())
        {
            stringBuilder.AppendLine(inspectString);
        }

        if (AttackVerb.verbProps.minRange > 0f)
        {
            stringBuilder.AppendLine("MinimumRange".Translate() + ": " + AttackVerb.verbProps.minRange.ToString("F0"));
        }

        switch (Spawned)
        {
            case true when IsMortarOrProjectileFliesOverhead && Position.Roofed(Map):
                stringBuilder.AppendLine("CannotFire".Translate() + ": " + "Roofed".Translate().CapitalizeFirst());
                break;
            case true when burstCooldownTicksLeft > 0 && burstCooldownTime() > 5f:
                stringBuilder.AppendLine("CanFireIn".Translate() + ": " +
                                         burstCooldownTicksLeft.ToStringSecondsFromTicks());
                break;
        }

        var compChangeableProjectile = gun.TryGetComp<CompChangeableProjectile>();
        if (compChangeableProjectile == null)
        {
            return stringBuilder.ToString().TrimEndNewlines();
        }

        if (compChangeableProjectile.Loaded)
        {
            stringBuilder.AppendLine("ShellLoaded".Translate(compChangeableProjectile.LoadedShell.LabelCap,
                compChangeableProjectile.LoadedShell));
        }
        else
        {
            stringBuilder.AppendLine("ShellNotLoaded".Translate());
        }

        return stringBuilder.ToString().TrimEndNewlines();
    }

    public override void DrawExtraSelectionOverlays()
    {
        var range = AttackVerb.verbProps.range;
        if (range < 90f)
        {
            GenDraw.DrawRadiusRing(Position, range);
        }

        var num = AttackVerb.verbProps.EffectiveMinRange(true);
        if (num is < 90f and > 0.1f)
        {
            GenDraw.DrawRadiusRing(Position, num);
        }

        if (WarmingUp)
        {
            var degreesWide = (int)(burstWarmupTicksLeft * 0.5f);
            GenDraw.DrawAimPie(this, CurrentTarget, degreesWide, def.size.x * 0.5f);
        }

        if (!forcedTarget.IsValid || forcedTarget.HasThing && !forcedTarget.Thing.Spawned)
        {
            return;
        }

        var b = !forcedTarget.HasThing ? forcedTarget.Cell.ToVector3Shifted() : forcedTarget.Thing.TrueCenter();
        var a = this.TrueCenter();
        b.y = AltitudeLayer.MetaOverlays.AltitudeFor();
        a.y = b.y;
        GenDraw.DrawLineBetween(a, b, ForcedTargetLineMat);
    }

    public override IEnumerable<Gizmo> GetGizmos()
    {
        foreach (var gizmo in base.GetGizmos())
        {
            yield return gizmo;
        }

        if (CanExtractShell)
        {
            var compChangeableProjectile = gun.TryGetComp<CompChangeableProjectile>();
            var commandAction = new Command_Action
            {
                defaultLabel = "CommandExtractShell".Translate(),
                defaultDesc = "CommandExtractShellDesc".Translate(),
                icon = compChangeableProjectile.LoadedShell.uiIcon,
                iconAngle = compChangeableProjectile.LoadedShell.uiIconAngle,
                iconOffset = compChangeableProjectile.LoadedShell.uiIconOffset,
                iconDrawScale = GenUI.IconDrawScale(compChangeableProjectile.LoadedShell),
                action = extractShell
            };
            yield return commandAction;
        }

        var compChangeableProjectile2 = gun.TryGetComp<CompChangeableProjectile>();
        if (compChangeableProjectile2 != null)
        {
            var storeSettings = compChangeableProjectile2.GetStoreSettings();
            foreach (var item in StorageSettingsClipboard.CopyPasteGizmosFor(storeSettings))
            {
                yield return item;
            }
        }

        if (CanSetForcedTarget)
        {
            var commandVerbTarget = new Command_VerbTarget
            {
                defaultLabel = "CommandSetForceAttackTarget".Translate(),
                defaultDesc = "CommandSetForceAttackTargetDesc".Translate(),
                icon = ContentFinder<Texture2D>.Get("UI/Commands/Attack"),
                verb = AttackVerb,
                hotKey = KeyBindingDefOf.Misc4,
                drawRadius = false
            };
            if (Spawned && IsMortarOrProjectileFliesOverhead && Position.Roofed(Map))
            {
                commandVerbTarget.Disable("CannotFire".Translate() + ": " + "Roofed".Translate().CapitalizeFirst());
            }

            yield return commandVerbTarget;
        }

        if (forcedTarget.IsValid)
        {
            var commandAction2 = new Command_Action
            {
                defaultLabel = "CommandStopForceAttack".Translate(),
                defaultDesc = "CommandStopForceAttackDesc".Translate(),
                icon = ContentFinder<Texture2D>.Get("UI/Commands/Halt"),
                action = delegate
                {
                    resetForcedTarget();
                    SoundDefOf.Tick_Low.PlayOneShotOnCamera();
                }
            };
            if (!forcedTarget.IsValid)
            {
                commandAction2.Disable("CommandStopAttackFailNotForceAttacking".Translate());
            }

            commandAction2.hotKey = KeyBindingDefOf.Misc5;
            yield return commandAction2;
        }

        if (!CanToggleHoldFire)
        {
            yield break;
        }

        var commandToggle = new Command_Toggle
        {
            defaultLabel = "CommandHoldFire".Translate(),
            defaultDesc = "CommandHoldFireDesc".Translate(),
            icon = ContentFinder<Texture2D>.Get("UI/Commands/HoldFire"),
            hotKey = KeyBindingDefOf.Misc6,
            toggleAction = delegate
            {
                holdFire = !holdFire;
                if (holdFire)
                {
                    resetForcedTarget();
                }
            },
            isActive = () => holdFire
        };
        yield return commandToggle;
    }

    private void extractShell()
    {
        GenPlace.TryPlaceThing(gun.TryGetComp<CompChangeableProjectile>().RemoveShell(), Position, Map,
            ThingPlaceMode.Near);
    }

    private void resetForcedTarget()
    {
        forcedTarget = LocalTargetInfo.Invalid;
        burstWarmupTicksLeft = 0;
        if (burstCooldownTicksLeft <= 0)
        {
            tryStartShootSomething(false);
        }
    }

    private void resetCurrentTarget()
    {
        currentTargetInt = LocalTargetInfo.Invalid;
        burstWarmupTicksLeft = 0;
    }

    private void makeGun()
    {
        gun = ThingMaker.MakeThing(def.building.turretGunDef);
        updateGunVerbs();
    }

    private void updateGunVerbs()
    {
        var allVerbs = gun.TryGetComp<CompEquippable>().AllVerbs;
        foreach (var verb in allVerbs)
        {
            verb.caster = this;
            verb.castCompleteCallback = burstComplete;
        }
    }

    public override void PreApplyDamage(ref DamageInfo dinfo, out bool absorbed)
    {
        base.PreApplyDamage(ref dinfo, out absorbed);
        if (absorbed)
        {
            return;
        }

        stunner.Notify_DamageApplied(dinfo);
        absorbed = false;
    }

    private void OnAttackedTarget(LocalTargetInfo target)
    {
        lastAttackTargetTick = Find.TickManager.TicksGame;
        lastAttackedTarget = target;
    }
}