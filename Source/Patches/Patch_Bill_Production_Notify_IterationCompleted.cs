using HarmonyLib;
using RimWorld;
using Verse;

namespace WorkbenchGroups.Patches
{
    /// <summary>
    /// Tells round robin a unit of a bill was finished, so orders that leave an unfinished item
    /// behind rotate once their item is done instead of when the job starts (see
    /// <see cref="Core.UnfinishedItemPolicy.RotatesAt"/>).
    ///
    /// On <c>Bill_Production</c> rather than <c>Bill_ProductionWithUft</c>: the latter overrides the
    /// method and calls base, so one postfix here covers both, and plain bills fall out of
    /// <c>RoundRobin.NotifyUnitCompleted</c> at its first check. A postfix, so the count has
    /// already been decremented and the bound item cleared by the time the list moves.
    /// </summary>
    [HarmonyPatch(typeof(Bill_Production), nameof(Bill_Production.Notify_IterationCompleted))]
    public static class Patch_Bill_Production_Notify_IterationCompleted
    {
        public static void Postfix(Bill_Production __instance)
        {
            RoundRobin.NotifyUnitCompleted(__instance);
        }
    }
}
