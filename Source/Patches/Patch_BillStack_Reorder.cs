using HarmonyLib;
using RimWorld;
using Verse;

namespace WorkbenchGroups.Patches
{
    /// <summary>
    /// Keeps a round-robin group's remembered ordering in step with the player dragging bills.
    ///
    /// Sharing the reorder itself needs no code: every member points at one <c>BillStack</c>, so
    /// moving a bill at one bench moves it at all of them by construction. The problem is the
    /// snapshot. Round robin is implemented by really rotating the list, so switching it off
    /// reprojects the list onto the order snapshotted when it was switched on — and a drag
    /// performed while round robin was running is not in that snapshot. Without this, arranging
    /// your orders and then switching back to "in order" silently threw the arrangement away and
    /// restored one from minutes earlier.
    ///
    /// A manual reorder is treated as the player re-authoring the order, so the snapshot is
    /// replaced with the list as it now stands. That does bake in whatever rotation round robin
    /// had applied, and that is the intended reading: the list they just arranged is the list
    /// they were looking at, so it is the one they meant.
    /// </summary>
    [HarmonyPatch(typeof(BillStack), nameof(BillStack.Reorder))]
    public static class Patch_BillStack_Reorder
    {
        public static void Postfix(BillStack __instance)
        {
            if (!(__instance?.billGiver is Building_WorkTable anchor) || !anchor.Spawned)
            {
                return;
            }

            BillGroupIndex index = BillGroupIndex.For(anchor.Map);
            if (index == null || !index.IsAnchor(anchor))
            {
                return;
            }

            CompBillGroup comp = anchor.GetComp<CompBillGroup>();
            if (comp == null || comp.Ordering != OrderingMode.RoundRobin)
            {
                return;
            }

            RoundRobin.ResnapshotCanonicalOrder(comp);
        }
    }
}
