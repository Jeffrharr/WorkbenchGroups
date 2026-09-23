using HarmonyLib;
using Verse;
using Verse.AI;

namespace WorkbenchGroups.Patches
{
    /// <summary>
    /// Stops haulers carrying off an unfinished item that is parked at a member bench of its own
    /// group.
    ///
    /// Vanilla leaves a bound unfinished item alone while its bill is the next one due and the
    /// item sits on or beside <c>bill.billStack.billGiver</c> — the owner's rect expanded by one.
    /// In a shared list the owner is always the anchor, so a half-sewn shirt set down at any other
    /// bench of the group reads as litter and is hauled to a stockpile, and the next resume has to
    /// fetch it back. This widens "beside the owner" to "beside any bench of the owner's group",
    /// via <see cref="UnfinishedItemSharing.ParkedGroupBench"/>.
    ///
    /// On <c>PawnCanAutomaticallyHaulFast_NewTemp</c> because both other entry points
    /// (<c>PawnCanAutomaticallyHaulFast</c> and <c>PawnCanAutomaticallyHaul</c>) funnel through
    /// it. A postfix that only ever turns yes into no, so it cannot make anything haulable that
    /// vanilla or another mod refused. The checks are ordered cheapest first: a type test that
    /// rejects nearly every thing on the map, then the group lookup, and last
    /// <c>FirstShouldDoNow</c>, which walks the bill stack.
    /// </summary>
    [HarmonyPatch(typeof(HaulAIUtility), nameof(HaulAIUtility.PawnCanAutomaticallyHaulFast_NewTemp))]
    public static class Patch_HaulAIUtility_PawnCanAutomaticallyHaulFast
    {
        public static void Postfix(Thing t, ref bool __result)
        {
            if (!__result || !(t is UnfinishedThing uft) || uft.BoundBill == null)
            {
                return;
            }

            if (UnfinishedItemSharing.ParkedGroupBench(uft) == null)
            {
                return;
            }

            // Same condition vanilla pairs with its footprint test: the item is only protected
            // while its bill is the next one the list would work.
            if (uft.BoundBill.billStack.FirstShouldDoNow == uft.BoundBill)
            {
                __result = false;
            }
        }
    }
}
