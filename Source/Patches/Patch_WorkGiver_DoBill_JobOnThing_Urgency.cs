using HarmonyLib;
using RimWorld;
using Verse;

namespace WorkbenchGroups.Patches
{
    /// <summary>
    /// Re-sorts a stock-aware group's shared list just before a bench is scanned. The reasoning
    /// for the hook point is on <see cref="UrgencySort"/>.
    ///
    /// A sibling patch class on the same method as <see cref="Patch_WorkGiver_DoBill_JobOnThing"/>
    /// rather than a line in its prefix. That class owns a field swap bracketed by a finalizer and
    /// has nothing in common with this but the method; keeping them apart means neither change
    /// has to be read to review the other. The two prefixes are independent — one swaps a timer
    /// field on each bill, this one reorders the list the bills are in — so their relative order
    /// does not matter, and none is declared.
    ///
    /// Touches only the list, never the returned job, for the same compatibility reason as its
    /// sibling: another popular mod replaces this method's result outright.
    /// </summary>
    [HarmonyPatch(typeof(WorkGiver_DoBill), nameof(WorkGiver_DoBill.JobOnThing))]
    public static class Patch_WorkGiver_DoBill_JobOnThing_Urgency
    {
        public static void Prefix(Thing thing)
        {
            UrgencySort.BeforeScan(thing);
        }
    }
}
