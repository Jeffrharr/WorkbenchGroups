using HarmonyLib;
using RimWorld;
using Verse;

namespace WorkbenchGroups.Patches
{
    /// <summary>
    /// Keeps a group's shared bill list from being written once per member.
    ///
    /// <c>Building_WorkTable.ExposeData</c> deep-saves its <c>billStack</c> unconditionally. With
    /// N benches pointing at one stack that writes the same bills N times, which warns on save,
    /// hard-errors on load with a duplicate load ID, and leaves a pawn's <c>job.bill</c> resolving
    /// to an arbitrary one of the copies. Swapping each member's own list back in for the duration
    /// of the save means every bench persists exactly what it owns, and the group's bills are
    /// written once, by the anchor, in a perfectly ordinary vanilla node.
    ///
    /// That last property is also what makes the mod removable: with it uninstalled, every bench
    /// loads a valid bill list and nothing dangles.
    /// </summary>
    [HarmonyPatch(typeof(Building_WorkTable), nameof(Building_WorkTable.ExposeData))]
    public static class Patch_Building_WorkTable_ExposeData
    {
        public static void Prefix(Building_WorkTable __instance, out CompBillGroup __state)
        {
            __state = null;

            if (Scribe.mode != LoadSaveMode.Saving)
            {
                return;
            }

            CompBillGroup comp = __instance.GetComp<CompBillGroup>();
            if (comp == null || !comp.IsMember)
            {
                return;
            }

            comp.BeginSaveSwap();
            __state = comp;
        }

        /// <summary>
        /// A finalizer rather than a postfix on purpose: it runs even if something further up
        /// throws. A postfix would not, and the bench would be left silently pointing at its own
        /// empty list — an unlink the player never asked for and would not notice for sessions.
        /// </summary>
        public static void Finalizer(CompBillGroup __state)
        {
            __state?.EndSaveSwap();
        }
    }
}
