using HarmonyLib;
using RimWorld;
using Verse;

namespace WorkbenchGroups.Patches
{
    /// <summary>
    /// Brackets each bench's work scan, for two jobs that both need to know which bench of a group
    /// is being looked at.
    ///
    /// 1. <b>Per-bench "no ingredients, retry later" timer.</b> See
    ///    <see cref="IngredientMuteIsolation"/> for why the shared timer is a problem. The pair
    ///    swaps the remembered per-bench value into vanilla's field around the scan and reads back
    ///    whatever vanilla decided, so no vanilla behaviour changes — only which bench the decision
    ///    is remembered against. Gated on the <c>isolateIngredientMute</c> setting.
    /// 2. <b>The scanned bench, for unfinished-item orders.</b> <c>FinishUftJob</c> is private and
    ///    only sees the bill, so the bench being scanned is handed across as a static via
    ///    <see cref="UnfinishedItemSharing.BeginScan"/>. Not gated on any setting: the redirect is
    ///    what makes those orders safe to share at all.
    ///
    /// We deliberately touch only state and never the return value. Another popular mod
    /// postfixes this same method and replaces the job it returns outright; leaving the result
    /// alone keeps the two compatible.
    /// </summary>
    [HarmonyPatch(typeof(WorkGiver_DoBill), nameof(WorkGiver_DoBill.JobOnThing))]
    public static class Patch_WorkGiver_DoBill_JobOnThing
    {
        public static void Prefix(Thing thing, out BillStack __state)
        {
            __state = null;

            if (!(thing is Building_WorkTable bench) || !bench.Spawned)
            {
                return;
            }

            // IsGrouped rather than GroupSize: one hash lookup instead of GetComp plus a
            // dictionary walk, on a method that runs per bench per pawn per work scan. This lookup
            // is now unconditional — it used to sit behind the setting check below — which is why
            // it had to be the near-free one.
            BillGroupIndex index = BillGroupIndex.For(bench.Map);
            if (index == null || !index.IsGrouped(bench))
            {
                return;
            }

            UnfinishedItemSharing.BeginScan(bench);

            if (WorkbenchGroupsMod.Settings?.isolateIngredientMute != true)
            {
                return;
            }

            IngredientMuteIsolation.LoadInto(bench.billStack, bench.thingIDNumber);
            __state = bench.billStack;
        }

        /// <summary>
        /// A finalizer so both jobs unwind even if the scan throws. Without it, one exception would
        /// leave every bill in the group carrying whichever bench's timer happened to be loaded,
        /// and would leave a stale scanned bench for the next, unrelated scan to redirect towards.
        ///
        /// <see cref="UnfinishedItemSharing.EndScan"/> is unconditional because it is one static
        /// write, cheaper than working out whether the prefix set anything.
        /// </summary>
        public static void Finalizer(Thing thing, BillStack __state)
        {
            UnfinishedItemSharing.EndScan();

            if (__state != null && thing != null)
            {
                IngredientMuteIsolation.StoreFrom(__state, thing.thingIDNumber);
            }
        }
    }
}
