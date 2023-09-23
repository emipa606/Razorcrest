using RimWorld;
using Verse;

namespace Razorcrest;

public class Verb_Shoot : Verb_LaunchProjectile
{
    protected override int ShotsPerBurst => verbProps.burstShotCount;

    public override void WarmupComplete()
    {
        base.WarmupComplete();
        if (currentTarget.Thing is not Pawn { Downed: false } pawn || !CasterIsPawn || CasterPawn.skills == null)
        {
            return;
        }

        var num = pawn.HostileTo(caster) ? 170f : 20f;
        var num2 = verbProps.AdjustedFullCycleTime(this, CasterPawn);
        CasterPawn.skills.Learn(SkillDefOf.Shooting, num * num2);
    }

    protected override bool TryCastShot()
    {
        var num = base.TryCastShot();
        if (num && CasterIsPawn)
        {
            CasterPawn.records.Increment(RecordDefOf.ShotsFired);
        }

        return num;
    }
}