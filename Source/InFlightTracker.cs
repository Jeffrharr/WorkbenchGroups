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
        /// Which pawns are working each bill, keyed by the Bill object itself.
        ///
        /// Holding the pawns rather than a bare count is what makes the backstop affordable. The
        /// count alone could only be checked against reality by scanning every pawn on the map;
        /// with the workers recorded, the common repair — a pawn that stopped without the
        /// decrement firing — is checked in O(pawns actually working bills), which is a handful
        /// rather than the whole colony.
        ///
        /// It also makes the tracking idempotent: a pawn already recorded against a bill cannot
        /// be counted twice, which a bare increment could do if a hook ever fired twice.
        /// </summary>
        private static readonly Dictionary<Bill, HashSet<Pawn>> workers
            = new Dictionary<Bill, HashSet<Pawn>>();

        public static int InFlight(Bill bill)
        {
            if (bill == null)
            {
                return 0;
            }

            return workers.TryGetValue(bill, out HashSet<Pawn> set) ? set.Count : 0;
        }

        public static void Increment(Bill bill, Pawn pawn)
        {
            if (bill == null || pawn == null)
            {
                return;
            }

            if (!workers.TryGetValue(bill, out HashSet<Pawn> set))
            {
                set = new HashSet<Pawn>();
                workers[bill] = set;
            }

            set.Add(pawn);
        }

        public static void Decrement(Bill bill, Pawn pawn)
        {
            if (bill == null || pawn == null || !workers.TryGetValue(bill, out HashSet<Pawn> set))
            {
                return;
            }

            set.Remove(pawn);

            if (set.Count == 0)
            {
                workers.Remove(bill);
            }
        }


        public static void Forget(Bill bill)
        {
            if (bill != null)
            {
                workers.Remove(bill);
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
        /// <summary>
        /// Drops workers that have stopped without the decrement firing.
        ///
        /// This is the cheap half of the backstop and the one that matters: an over-count wedges
        /// a bill permanently, because the overshoot guard keeps refusing to start it. Checking
        /// costs one <c>CurJob</c> read per recorded worker — a handful, not the colony — so it
        /// can run often.
        ///
        /// Deliberately does not look for *under*-counting; see <see cref="ReconcileFully"/>.
        /// </summary>
        public static void PruneStoppedWorkers(Map map)
        {
            if (map == null || workers.Count == 0)
            {
                return;
            }

            List<Bill> emptied = null;

            foreach (KeyValuePair<Bill, HashSet<Pawn>> entry in workers)
            {
                // Only this map's bills: reconciling one map must not drop counts on another,
                // and Bill.Map resolves through the stack's owning bench.
                if (entry.Key.Map == map)
                {
                    entry.Value.RemoveWhere(pawn => !IsStillWorking(pawn, entry.Key));

                    if (entry.Value.Count == 0)
                    {
                        emptied = emptied ?? new List<Bill>();
                        emptied.Add(entry.Key);
                    }
                }
            }

            if (emptied != null)
            {
                foreach (Bill bill in emptied)
                {
                    workers.Remove(bill);
                }
            }
        }

        private static bool IsStillWorking(Pawn pawn, Bill bill)
        {
            return pawn != null && pawn.Spawned && !pawn.Dead && pawn.CurJob?.bill == bill;
        }

        /// <summary>
        /// The expensive half: a full scan that also finds pawns working a bill we never recorded.
        ///
        /// A missed increment is the milder failure — the guard is merely too permissive, so the
        /// group can overproduce exactly the way vanilla would — which is why this runs an order
        /// of magnitude less often than the prune. It is kept at all because "silently behaves
        /// like vanilla" is still a bug, just not one that strands the player's orders.
        /// </summary>
        public static void ReconcileFully(Map map)
        {
            if (map == null)
            {
                return;
            }

            PruneStoppedWorkers(map);

            foreach (Pawn pawn in map.mapPawns.AllPawnsSpawned)
            {
                Bill bill = pawn.CurJob?.bill;
                if (bill != null)
                {
                    Increment(bill, pawn);
                }
            }
        }
    }
}
