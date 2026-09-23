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

        /// <summary>The pawn that works the unfinished-item order in the craft-loop scenarios.</summary>
        public static Pawn Crafter;

        /// <summary>The pawn given only hauling, to see whether it leaves a parked item alone.</summary>
        public static Pawn Hauler;

        /// <summary>A pawn with no work at all, used to hold one bench busy.</summary>
        public static Pawn Blocker;

        /// <summary>
        /// A loose item placed where the hauler should take it. Proves the hauler was actually
        /// hauling, so "the unfinished item stayed put" is a finding and not an idle pawn.
        /// </summary>
        public static Thing ControlItem;

        /// <summary>thingIDNumber of the unfinished item when it was remembered, or -1.</summary>
        public static int RememberedUftId = -1;

        public static void Reset()
        {
            Benches.Clear();
            Bills.Clear();
            SimulatedWorkers.Clear();
            Crafter = null;
            Hauler = null;
            Blocker = null;
            ControlItem = null;
            RememberedUftId = -1;
        }
    }
}
