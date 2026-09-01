using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace WorkbenchGroups
{
    /// <summary>
    /// Counts how many pawns are currently working each bill.
    ///
    /// This is the number that prevents overshoot. Vanilla never needed it: a bill belonged to
    /// one bench, so at most one pawn could ever be working it. Once benches share a list, three
    /// idle cooks all take "make 1 fine meal" and make three.
    ///
    /// It is maintained incrementally rather than computed on demand because the natural place to
    /// ask is <c>Bill_Production.ShouldDoNow</c>, and that is called for every bill giver on the
    /// map, for every pawn, on every work scan — plus once per bill per frame while the tab is
    /// open. A pawn scan inside it would make work-giving O(benches x bills x pawns).
    /// </summary>
    public static class InFlightTracker
    {
        /// <summary>
        /// Keyed by the Bill object itself. Entries are removed on decrement and when a bill is
        /// deleted, so this does not pin dead bills; the periodic reconcile is the backstop.
        /// </summary>
        private static readonly Dictionary<Bill, int> counts = new Dictionary<Bill, int>();

        public static int InFlight(Bill bill)
        {
            if (bill == null)
            {
                return 0;
            }

            return counts.TryGetValue(bill, out int value) ? value : 0;
        }

        public static void Increment(Bill bill)
        {
            if (bill == null)
            {
                return;
            }

            counts.TryGetValue(bill, out int value);
            counts[bill] = value + 1;
        }

        public static void Decrement(Bill bill)
        {
            if (bill == null || !counts.TryGetValue(bill, out int value))
            {
                return;
            }

            if (value <= 1)
            {
                counts.Remove(bill);
                return;
            }

            counts[bill] = value - 1;
        }

        public static void Forget(Bill bill)
        {
            if (bill != null)
            {
                counts.Remove(bill);
            }
        }

        /// <summary>
        /// Recomputes the counts for one map from live pawn jobs.
        ///
        /// Two details are load-bearing. The predicate is <c>job.bill == bill</c> with no check on
        /// the job's def, because <c>Job.bill</c> is only ever set for bill work and other mods run
        /// their own bill JobDefs — filtering on <c>JobDefOf.DoBill</c> would count none of theirs.
        /// And it walks every spawned pawn rather than free colonists, because mechs and slaves do
        /// bills too and a bill can be restricted to exactly those.
        /// </summary>
        public static void Reconcile(Map map)
        {
            if (map == null)
            {
                return;
            }

            Dictionary<Bill, int> observed = new Dictionary<Bill, int>();
            foreach (Pawn pawn in map.mapPawns.AllPawnsSpawned)
            {
                Bill bill = pawn.CurJob?.bill;
                if (bill != null)
                {
                    observed.TryGetValue(bill, out int seen);
                    observed[bill] = seen + 1;
                }
            }

            // Only touch entries belonging to this map, so reconciling one map does not wipe
            // counts on another. Bill.Map resolves through the stack's owning bench.
            List<Bill> stale = new List<Bill>();
            foreach (KeyValuePair<Bill, int> entry in counts)
            {
                bool onThisMap = entry.Key.Map == map;
                bool matchesObservation = observed.TryGetValue(entry.Key, out int actual) && actual == entry.Value;
                if (onThisMap && !matchesObservation)
                {
                    stale.Add(entry.Key);
                }
            }

            foreach (Bill bill in stale)
            {
                if (observed.TryGetValue(bill, out int actual) && actual > 0)
                {
                    counts[bill] = actual;
                }
                else
                {
                    counts.Remove(bill);
                }
            }

            foreach (KeyValuePair<Bill, int> entry in observed)
            {
                if (!counts.ContainsKey(entry.Key))
                {
                    counts[entry.Key] = entry.Value;
                }
            }
        }
    }
}
