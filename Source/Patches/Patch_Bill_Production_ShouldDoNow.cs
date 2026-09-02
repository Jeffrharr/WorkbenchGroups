using HarmonyLib;
using RimWorld;
using Verse;
using WorkbenchGroups.Core;

namespace WorkbenchGroups.Patches
{
    /// <summary>
    /// Stops a group overproducing by counting work already underway against the bill's target.
    ///
    /// This only ever tightens vanilla's answer — it can turn a yes into a no, never the reverse —
    /// so with the setting off, or on an ungrouped bench, behaviour is exactly vanilla.
    ///
    /// Kept cheap deliberately. <c>ShouldDoNow</c> is called for every bill giver on the map, for
    /// every pawn, on every work scan, and once per bill per frame while the tab is open, so the
    /// common path here is two dictionary-free early exits. The only expensive call — recounting
    /// products for a "do until you have X" bill — happens solely when someone is actually working
    /// that bill, which is a handful of bills at most.
    /// </summary>
    [HarmonyPatch(typeof(Bill_Production), nameof(Bill_Production.ShouldDoNow))]
    public static class Patch_Bill_Production_ShouldDoNow
    {
        public static void Postfix(Bill_Production __instance, ref bool __result)
        {
            if (!__result || WorkbenchGroupsMod.Settings?.preventOvershoot != true)
            {
                return;
            }

            int inFlight = InFlightTracker.InFlight(__instance);
            if (inFlight <= 0)
            {
                return;
            }

            if (!IsGroupedBill(__instance))
            {
                return;
            }

            RepeatModeCode mode = BenchEligibility.RepeatModeOf(__instance);

            // Only recount products when the answer can actually depend on it. Vanilla's counter
            // walks the map's things for filtered bills, so calling it unconditionally here would
            // be the expensive mistake this whole design is arranged to avoid.
            int produced = mode == RepeatModeCode.TargetCount
                ? __instance.recipe.WorkerCounter.CountProducts(__instance)
                : 0;

            __result = OvershootPolicy.MayStartAnother(
                mode,
                __instance.repeatCount,
                produced,
                __instance.targetCount,
                inFlight,
                __instance.paused,
                __instance.suspended);
        }

        /// <summary>
        /// Vanilla can never have two pawns on one bill, so applying the guard off-group would
        /// change behaviour nobody asked us to change.
        /// </summary>
        private static bool IsGroupedBill(Bill bill)
        {
            if (!(bill.billStack?.billGiver is Building_WorkTable bench) || !bench.Spawned)
            {
                return false;
            }

            BillGroupIndex index = BillGroupIndex.For(bench.Map);
            return index != null && index.IsGrouped(bench);
        }
    }
}
