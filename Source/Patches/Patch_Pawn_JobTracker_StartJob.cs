using HarmonyLib;
using Verse.AI;

namespace WorkbenchGroups.Patches
{
    /// <summary>
    /// The single hook for both of this mod's behaviours, because both need the same moment: a
    /// pawn has committed to a bill.
    ///
    /// Chosen over the more obvious alternatives for concrete reasons.
    /// <c>Bill.Notify_DoBillStarted</c> fires up to a tick later, from the job driver's first
    /// toil, and is skipped entirely by at least one mod's replacement driver.
    /// <c>WorkGiver_DoBill.JobOnThing</c> also runs while building a right-click menu, so hooking
    /// it would rotate the player's bill list merely because they right-clicked a bench.
    ///
    /// Keying off <c>job.bill</c> rather than the job's def is what keeps this working with mods
    /// that run bill work under their own JobDef.
    /// </summary>
    [HarmonyPatch(typeof(Pawn_JobTracker), nameof(Pawn_JobTracker.StartJob))]
    public static class Patch_Pawn_JobTracker_StartJob
    {
        public static void Postfix(Job newJob)
        {
            if (newJob?.bill == null)
            {
                return;
            }

            InFlightTracker.Increment(newJob.bill);
            RoundRobin.NotifyBillStarted(newJob.bill);
        }
    }
}
