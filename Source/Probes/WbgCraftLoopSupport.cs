using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using Verse.AI;

namespace WorkbenchGroups.Probes
{
    /// <summary>
    /// Shared lookups for the craft-loop steps and probes (issue #11): which tracked bench is the
    /// anchor, which is the member, and where the order's unfinished item is.
    ///
    /// Everything is reported as a <b>role</b> rather than a bench index — 0 anchor, 1 member,
    /// -1 neither — because anchor election picks by thing ID, which a scenario cannot know in
    /// advance. "The item is at the member" is the claim under test, so that is what the probes
    /// say.
    /// </summary>
    public static class WbgCraftLoopSupport
    {
        public const float AtAnchor = 0f;
        public const float AtMember = 1f;
        public const float AtNeither = -1f;
        public const float Missing = -2f;

        public static Building_WorkTable Anchor(Map map)
        {
            if (WbgTestState.Benches.Count == 0)
            {
                return null;
            }

            return BillGroupIndex.For(map)?.AnchorOf(WbgTestState.Benches[0]);
        }

        /// <summary>The first tracked bench that is not the anchor.</summary>
        public static Building_WorkTable Member(Map map)
        {
            Building_WorkTable anchor = Anchor(map);
            return WbgTestState.Benches.FirstOrDefault(bench => bench != anchor);
        }

        public static float RoleOf(Map map, Thing thing)
        {
            if (thing == null)
            {
                return AtNeither;
            }

            if (thing == Anchor(map))
            {
                return AtAnchor;
            }

            return thing == Member(map) ? AtMember : AtNeither;
        }

        /// <summary>
        /// The tracked bench whose footprint, expanded by one, holds <paramref name="cell"/> —
        /// vanilla's own "resting on or beside the bench" test.
        /// </summary>
        public static float RoleAt(Map map, IntVec3 cell)
        {
            foreach (Building_WorkTable bench in WbgTestState.Benches)
            {
                if (bench.Spawned && bench.OccupiedRect().ExpandedBy(1).Contains(cell))
                {
                    return RoleOf(map, bench);
                }
            }

            return AtNeither;
        }

        /// <summary>The first queued order's unfinished item, or null.</summary>
        public static UnfinishedThing BoundUft()
        {
            Bill_ProductionWithUft bill = WbgTestState.Bills.OfType<Bill_ProductionWithUft>().FirstOrDefault();
            return bill?.BoundUft;
        }

        /// <summary>
        /// The DoBill work giver that serves <paramref name="bench"/>'s def — the real vanilla
        /// worker, so a probe calling it goes through every Harmony patch on the way.
        /// </summary>
        public static WorkGiver_DoBill WorkGiverFor(Building_WorkTable bench)
        {
            if (bench == null)
            {
                return null;
            }

            foreach (WorkGiverDef def in DefDatabase<WorkGiverDef>.AllDefsListForReading)
            {
                if (def.Worker is WorkGiver_DoBill worker
                    && def.fixedBillGiverDefs != null
                    && def.fixedBillGiverDefs.Contains(bench.def))
                {
                    return worker;
                }
            }

            return null;
        }

        /// <summary>A "dx,dz" offset from the map centre.</summary>
        public static bool TryParseCell(Map map, string raw, out IntVec3 cell)
        {
            cell = IntVec3.Invalid;
            string[] parts = (raw ?? "").Split(',');
            if (parts.Length != 2
                || !int.TryParse(parts[0].Trim(), out int dx)
                || !int.TryParse(parts[1].Trim(), out int dz))
            {
                return false;
            }

            if (map != null)
            {
                cell = map.Center + new IntVec3(dx, 0, dz);
            }

            return true;
        }

        public static Pawn PawnFor(string role)
        {
            switch (role)
            {
                case "crafter": return WbgTestState.Crafter;
                case "hauler": return WbgTestState.Hauler;
                case "blocker": return WbgTestState.Blocker;
                default: return null;
            }
        }

        public static bool IsKnownRole(string role)
        {
            return role == "crafter" || role == "hauler" || role == "blocker";
        }

        /// <summary>Free, able colonists, nearest the map centre first — the ones a scenario spawned.</summary>
        public static List<Pawn> CandidateColonists(Map map)
        {
            IntVec3 centre = map.Center;
            return map.mapPawns.FreeColonistsSpawned
                .Where(pawn => !pawn.Downed && !pawn.Dead && pawn.workSettings != null)
                .OrderBy(pawn => pawn.Position.DistanceToSquared(centre))
                .ToList();
        }
    }
}
