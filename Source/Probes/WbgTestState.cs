using System.Collections.Generic;
using RimWorld;
using Verse;

namespace WorkbenchGroups.Probes
{
    /// <summary>
    /// Scratch state shared between this mod's scenario steps and its probes.
    ///
    /// Probes are identified by name alone and take no arguments, so anything a probe needs to
    /// know about the scene — which benches the scenario built, which bills it queued — has to be
    /// recorded by the step that built it.
    ///
    /// Dev-only: this type is compiled into the probes bridge, never into the shipped mod.
    /// </summary>
    public static class WbgTestState
    {
        /// <summary>Benches the scenario linked, in the order it found them.</summary>
        public static readonly List<Building_WorkTable> Benches = new List<Building_WorkTable>();

        /// <summary>Bills the scenario queued, in the order it queued them.</summary>
        public static readonly List<Bill_Production> Bills = new List<Bill_Production>();

        /// <summary>Pawns used to simulate a worker taking a bill, so each start is a distinct claim.</summary>
        public static readonly List<Pawn> SimulatedWorkers = new List<Pawn>();

        public static void Reset()
        {
            Benches.Clear();
            Bills.Clear();
            SimulatedWorkers.Clear();
        }
    }
}
