using System.Reflection;
using HarmonyLib;
using Verse.AI;

namespace WorkbenchGroups.Patches
{
    /// <summary>
    /// Releases a bill's reservation when the pawn stops working it, however that happened.
    ///
    /// <c>CleanupCurrentJob</c> is private, but it is the one funnel every job ending passes
    /// through — finishing, being interrupted, being replaced, the pawn being downed. Hooking the
    /// public <c>EndCurrentJob</c> instead would miss the replacement path and slowly leak
    /// reservations until a bill could never be started again.
    ///
    /// It must be a prefix: the method clears <c>curJob</c>, so by the time a postfix ran there
    /// would be nothing left to identify.
    /// </summary>
    [HarmonyPatch]
    public static class Patch_Pawn_JobTracker_CleanupCurrentJob
    {
        public static MethodBase TargetMethod()
        {
            return AccessTools.Method(typeof(Pawn_JobTracker), "CleanupCurrentJob");
        }

        public static void Prefix(Pawn_JobTracker __instance)
        {
            if (__instance.curJob?.bill != null)
            {
                InFlightTracker.Decrement(__instance.curJob.bill);
            }
        }
    }
}
