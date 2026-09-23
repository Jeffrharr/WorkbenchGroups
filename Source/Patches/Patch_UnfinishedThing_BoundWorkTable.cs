using HarmonyLib;
using Verse;

namespace WorkbenchGroups.Patches
{
    /// <summary>
    /// Reports the group bench an unfinished item is parked at as its work table, rather than the
    /// bench that owns the shared list.
    ///
    /// <c>BoundWorkTable</c> reads <c>BoundBill.billStack.billGiver</c>, which in a shared list is
    /// always the anchor. Two readers care: the haul placement validator
    /// (<c>HaulAIUtility</c>), which refuses to set goods down on the item's bench or its
    /// interaction cell, and the selection overlay, which draws a line from the item to its
    /// bench. Answering with the member bench the item actually sits at fixes both — haulers stop
    /// piling goods onto the bench holding it, and the line points somewhere true.
    ///
    /// Falls through to vanilla's answer whenever the item is not parked at a group bench (lying
    /// in a stockpile, carried, or its bench is in no group), so behaviour outside groups is
    /// unchanged. The anchor is itself a group bench, so an item parked there gets the same
    /// answer vanilla gives.
    /// </summary>
    [HarmonyPatch(typeof(UnfinishedThing), nameof(UnfinishedThing.BoundWorkTable), MethodType.Getter)]
    public static class Patch_UnfinishedThing_BoundWorkTable
    {
        public static void Postfix(UnfinishedThing __instance, ref Thing __result)
        {
            // Null means the bill or its bench is gone; nothing of ours to substitute.
            if (__result == null)
            {
                return;
            }

            Thing parked = UnfinishedItemSharing.ParkedGroupBench(__instance);
            if (parked != null)
            {
                __result = parked;
            }
        }
    }
}
