using HarmonyLib;
using RimWorld;

namespace WorkbenchGroups.Patches
{
    /// <summary>
    /// Drops our per-bill bookkeeping when the player deletes a bill.
    ///
    /// Both tables are keyed by the Bill object, so without this they would keep dead bills alive
    /// for as long as the game runs. Deleting bills is routine, so the leak would be steady rather
    /// than theoretical.
    ///
    /// The group's "do this next" marker is dropped here too. Unlike the two tables that is not a
    /// leak — the marker is a load ID rather than a reference, and <c>NextOrder.Resolve</c>
    /// discards one that names nothing the next time anyone reads it. Clearing it here is about
    /// latency, not correctness: it means the highlighted row goes the instant the order does
    /// rather than the next time something happens to look.
    /// </summary>
    [HarmonyPatch(typeof(BillStack), nameof(BillStack.Delete))]
    public static class Patch_BillStack_Delete
    {
        public static void Postfix(BillStack __instance, Bill bill)
        {
            InFlightTracker.Forget(bill);
            IngredientMuteIsolation.Forget(bill);
            NextOrder.ForgetIfMarked(__instance, bill);
        }
    }
}
