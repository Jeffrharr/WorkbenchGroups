using HarmonyLib;
using RimWorld;
using Verse;

namespace WorkbenchGroups.Patches
{
    /// <summary>
    /// Gives each bench in a group its own "no ingredients, retry later" timer.
    ///
    /// See <see cref="IngredientMuteIsolation"/> for why the shared timer is a problem. This pair
    /// swaps the remembered per-bench value into vanilla's field around the scan and reads back
    /// whatever vanilla decided, so no vanilla behaviour changes — only which bench the decision
    /// is remembered against.
    ///
    /// We deliberately touch only the field and never the return value. Another popular mod
    /// postfixes this same method and replaces the job it returns outright; leaving the result
    /// alone keeps the two compatible.
    /// </summary>
    [HarmonyPatch(typeof(WorkGiver_DoBill), nameof(WorkGiver_DoBill.JobOnThing))]
    public static class Patch_WorkGiver_DoBill_JobOnThing
    {
        public static void Prefix(Thing thing, out BillStack __state)
        {
            __state = null;

            if (WorkbenchGroupsMod.Settings?.isolateIngredientMute != true)
            {
                return;
            }

            if (!(thing is Building_WorkTable bench) || !bench.Spawned)
            {
                return;
            }

            // IsGrouped rather than GroupSize: one hash lookup instead of GetComp plus a
            // dictionary walk, on a method that runs per bench per pawn per work scan.
            BillGroupIndex index = BillGroupIndex.For(bench.Map);
            if (index == null || !index.IsGrouped(bench))
            {
                return;
            }

            IngredientMuteIsolation.LoadInto(bench.billStack, bench.thingIDNumber);
            __state = bench.billStack;
        }

        /// <summary>
        /// A finalizer so the timer is stored back even if the scan throws — otherwise one
        /// exception would leave every bill in the group carrying whichever bench's timer happened
        /// to be loaded at the time.
        /// </summary>
        public static void Finalizer(Thing thing, BillStack __state)
        {
            if (__state != null && thing != null)
            {
                IngredientMuteIsolation.StoreFrom(__state, thing.thingIDNumber);
            }
        }
    }
}
