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

    public static readonly Material ForcedTargetLineMat =
        MaterialPool.MatFrom(GenDraw.LineTexPath, ShaderDatabase.Transparent, new Color(1f, 0.5f, 0.5f));

    protected int burstCooldownTicksLeft;

    protected int burstWarmupTicksLeft;

    protected LocalTargetInfo currentTargetInt = LocalTargetInfo.Invalid;

    protected CompCanBeDormant dormantComp;

    protected LocalTargetInfo forcedTarget = LocalTargetInfo.Invalid;

    public Thing gun;

    private bool holdFire;

    protected CompInitiatable initiatableComp;

    private LocalTargetInfo lastAttackedTarget;

    private int lastAttackTargetTick;

    protected CompMannable mannableComp;

    protected CompPowerTrader powerComp;

    protected Effecter progressBarEffecter;

    protected StunHandler stunner;

    public ShipWithTurret()
    {
        stunner = new StunHandler(this);
    }

    public bool Active
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

    public CompEquippable GunCompEq => gun.TryGetComp<CompEquippable>();

    public LocalTargetInfo CurrentTarget => currentTargetInt;

    private bool WarmingUp => burstWarmupTicksLeft > 0;

    public Verb AttackVerb => GunCompEq.PrimaryVerb;

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

        UpdateGunVerbs();
    }

    public override void PostMake()
    {
        base.PostMake();
        MakeGun();
    }

    public override void DeSpawn(DestroyMode mode = DestroyMode.Vanish)
    {
        base.DeSpawn(mode);
        ResetCurrentTarget();
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
            UpdateGunVerbs();
        }
    }

    public override bool ClaimableBy(Faction by, StringBuilder reason = null)
    {
        if (!base.ClaimableBy(by, reason))
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
                ResetForcedTarget();
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
                TryStartShootSomething(false);
            }
        }

        if (holdFire)
        {
            Messages.Message("MessageTurretWontFireBecauseHoldFire".Translate(def.label), this,
                MessageTypeDefOf.RejectInput, false);
        }
    }

    public override void Tick()
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
                ExtractShell();
            }
        }

        if (forcedTarget.IsValid && !CanSetForcedTarget)
        {
            ResetForcedTarget();
        }

        if (!CanToggleHoldFire)
        {
            holdFire = false;
        }

        if (forcedTarget.ThingDestroyed)
        {
            ResetForcedTarget();
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
                    BeginBurst();
                }
            }
            else
            {
                if (burstCooldownTicksLeft > 0)
                {
                    burstCooldownTicksLeft--;
                    if (IsMortar)
                    {
                        if (progressBarEffecter == null)
                        {
                            progressBarEffecter = EffecterDefOf.ProgressBar.Spawn();
                        }

                        progressBarEffecter.EffectTick(this, TargetInfo.Invalid);
                        var mote = ((SubEffecter_ProgressBar)progressBarEffecter.children[0]).mote;
                        mote.progress = 1f - (Math.Max(burstCooldownTicksLeft, 0) /
                                              (float)BurstCooldownTime().SecondsToTicks());
                        mote.offsetZ = -0.8f;
                    }
                }

                if (burstCooldownTicksLeft <= 0 && this.IsHashIntervalTick(10))
                {
                    TryStartShootSomething(true);
                }
            }
        }
        else
        {
            ResetCurrentTarget();
        }
    }

    protected void TryStartShootSomething(bool canBeginBurstImmediately)
    {
        if (progressBarEffecter != null)
        {
            progressBarEffecter.Cleanup();
            progressBarEffecter = null;
        }

        if (!Spawned || holdFire && CanToggleHoldFire ||
            AttackVerb.ProjectileFliesOverhead() && Map.roofGrid.Roofed(Position) || !AttackVerb.Available())
        {
            ResetCurrentTarget();
            return;
        }

        var isValid = currentTargetInt.IsValid;
        currentTargetInt = forcedTarget.IsValid ? forcedTarget : TryFindNewTarget();

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
                BeginBurst();
            }
            else
            {
                burstWarmupTicksLeft = 1;
            }
        }
        else
        {
            ResetCurrentTarget();
        }
    }

    protected LocalTargetInfo TryFindNewTarget()
    {
        var attackTargetSearcher = TargSearcher();
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
            IsValidTarget);
    }

    private IAttackTargetSearcher TargSearcher()
    {
        if (mannableComp is { MannedNow: true })
        {
            return mannableComp.ManningPawn;
        }

        return this;
    }

    private bool IsValidTarget(Thing t)
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
            Log.Message(AttackVerb + " - Cant find a shoot line");
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

    protected void BeginBurst()
    {
        AttackVerb.TryStartCastOn(CurrentTarget);
        OnAttackedTarget(CurrentTarget);
    }

    protected void BurstComplete()
    {
        burstCooldownTicksLeft = BurstCooldownTime().SecondsToTicks();
    }

    protected float BurstCooldownTime()
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

        if (Spawned && IsMortarOrProjectileFliesOverhead && Position.Roofed(Map))
        {
            stringBuilder.AppendLine("CannotFire".Translate() + ": " + "Roofed".Translate().CapitalizeFirst());
        }
        else if (Spawned && burstCooldownTicksLeft > 0 && BurstCooldownTime() > 5f)
        {
            stringBuilder.AppendLine("CanFireIn".Translate() + ": " +
                                     burstCooldownTicksLeft.ToStringSecondsFromTicks());
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
            var command_Action = new Command_Action
            {
                defaultLabel = "CommandExtractShell".Translate(),
                defaultDesc = "CommandExtractShellDesc".Translate(),
                icon = compChangeableProjectile.LoadedShell.uiIcon,
                iconAngle = compChangeableProjectile.LoadedShell.uiIconAngle,
                iconOffset = compChangeableProjectile.LoadedShell.uiIconOffset,
                iconDrawScale = GenUI.IconDrawScale(compChangeableProjectile.LoadedShell),
                action = ExtractShell
            };
            yield return command_Action;
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
            var command_VerbTarget = new Command_VerbTarget
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
                command_VerbTarget.Disable("CannotFire".Translate() + ": " + "Roofed".Translate().CapitalizeFirst());
            }

            yield return command_VerbTarget;
        }

        if (forcedTarget.IsValid)
        {
            var command_Action2 = new Command_Action
            {
                defaultLabel = "CommandStopForceAttack".Translate(),
                defaultDesc = "CommandStopForceAttackDesc".Translate(),
                icon = ContentFinder<Texture2D>.Get("UI/Commands/Halt"),
                action = delegate
                {
                    ResetForcedTarget();
                    SoundDefOf.Tick_Low.PlayOneShotOnCamera();
                }
            };
            if (!forcedTarget.IsValid)
            {
                command_Action2.Disable("CommandStopAttackFailNotForceAttacking".Translate());
            }

            command_Action2.hotKey = KeyBindingDefOf.Misc5;
            yield return command_Action2;
        }

        if (!CanToggleHoldFire)
        {
            yield break;
        }

        var command_Toggle = new Command_Toggle
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
                    ResetForcedTarget();
                }
            },
            isActive = () => holdFire
        };
        yield return command_Toggle;
    }

    private void ExtractShell()
    {
        GenPlace.TryPlaceThing(gun.TryGetComp<CompChangeableProjectile>().RemoveShell(), Position, Map,
            ThingPlaceMode.Near);
    }

    private void ResetForcedTarget()
    {
        forcedTarget = LocalTargetInfo.Invalid;
        burstWarmupTicksLeft = 0;
        if (burstCooldownTicksLeft <= 0)
        {
            TryStartShootSomething(false);
        }
    }

    private void ResetCurrentTarget()
    {
        currentTargetInt = LocalTargetInfo.Invalid;
        burstWarmupTicksLeft = 0;
    }

    public void MakeGun()
    {
        gun = ThingMaker.MakeThing(def.building.turretGunDef);
        UpdateGunVerbs();
    }

    private void UpdateGunVerbs()
    {
        var allVerbs = gun.TryGetComp<CompEquippable>().AllVerbs;
        foreach (var verb in allVerbs)
        {
            verb.caster = this;
            verb.castCompleteCallback = BurstComplete;
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

    protected void OnAttackedTarget(LocalTargetInfo target)
    {
        lastAttackTargetTick = Find.TickManager.TicksGame;
        lastAttackedTarget = target;
    }
}