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
    /// </summary>
    [HarmonyPatch(typeof(BillStack), nameof(BillStack.Delete))]
    public static class Patch_BillStack_Delete
    {
        public static void Postfix(Bill bill)
        {
            InFlightTracker.Forget(bill);
            IngredientMuteIsolation.Forget(bill);
        }
    }
}
