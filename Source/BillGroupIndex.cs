using System.Collections.Generic;
using RimWorld;
using Verse;

namespace WorkbenchGroups
{
    /// <summary>
    /// Answers "who else is in this bench's group" by reading everyone's comps.
    ///
    /// Entirely derived state — nothing here is saved. Membership lives on the comps
    /// (see <see cref="CompBillGroup"/>); this only caches the reverse mapping, which is the
    /// direction the comps cannot answer on their own. Rebuilding rather than maintaining
    /// incremental adds and removes trades a rare O(benches) scan for not having a dozen call
    /// sites that can each forget to update the cache.
    /// </summary>
    public class BillGroupIndex : MapComponent
    {
        private readonly Dictionary<Building_WorkTable, List<Building_WorkTable>> membersByAnchor
            = new Dictionary<Building_WorkTable, List<Building_WorkTable>>();

        /// <summary>
        /// Per anchor, the recipes every member of its group can make.
        ///
        /// Exists purely so the bill list's chain icon is an O(1) set lookup. It used to ask the
        /// question per row per frame by walking the roster and calling
        /// <c>def.AllRecipes.Contains</c> on each member — a linear scan of up to seventy recipes,
        /// repeated for every visible row, sixty times a second. Built lazily on first ask and
        /// thrown away with the rest of the index, so it cannot drift from membership.
        /// </summary>
        private readonly Dictionary<Building_WorkTable, HashSet<RecipeDef>> commonRecipesByAnchor
            = new Dictionary<Building_WorkTable, HashSet<RecipeDef>>();

        /// <summary>Anchor-first rosters, cached because the draw code asks every frame.</summary>
        private readonly Dictionary<Building_WorkTable, List<Building_WorkTable>> rosterByAnchor
            = new Dictionary<Building_WorkTable, List<Building_WorkTable>>();

        /// <summary>
        /// Every bench that is in a group of two or more, anchors included.
        ///
        /// Exists so the two hottest paths — the work-giver prefix and the ShouldDoNow postfix —
        /// can ask "does this bench matter to us" with one hash lookup. Both used to route through
        /// <c>GroupSize</c> -> <c>AnchorOf</c> -> <c>Thing.GetComp</c>, and GetComp is a linear
        /// walk of the thing's comp list with a type test per element, run per bench per pawn per
        /// work scan.
        /// </summary>
        private readonly HashSet<Building_WorkTable> groupedBenches
            = new HashSet<Building_WorkTable>();

        /// <summary>Shared empty result, so the ungrouped case allocates nothing at all.</summary>
        private static readonly List<Building_WorkTable> EmptyRoster = new List<Building_WorkTable>();

        private bool dirty = true;

        /// <summary>How often stopped workers are pruned. Cheap: O(pawns actually working bills).</summary>
        private const int PruneInterval = 250;

        /// <summary>
        /// How often the full pawn scan runs. Ten times rarer than the prune because it is the
        /// expensive one — it walks every spawned pawn — and it only catches the milder failure.
        /// </summary>
        private const int FullReconcileInterval = 2500;

        public BillGroupIndex(Map map) : base(map)
        {
        }

        /// <summary>Last map asked for, and its index. See <see cref="For"/>.</summary>
        private static Map lastQueriedMap;
        private static BillGroupIndex lastQueriedIndex;

        /// <summary>
        /// The index for a map.
        ///
        /// Memoised on the last map asked for, because <c>Map.GetComponent</c> walks the map's
        /// whole component list and this is called from the two hottest paths in the mod — the
        /// work-giver prefix (per bench, per pawn, per scan) and the ShouldDoNow postfix. A
        /// one-entry memo rather than a dictionary keyed by Map: calls arrive in bursts for one
        /// map, a dictionary would hold Map references alive after the map is removed, and there
        /// is no map-removed hook to prune it from. Wrong-map asks simply miss and fall through.
        /// </summary>
        public static BillGroupIndex For(Map map)
        {
            if (map == null)
            {
                return null;
            }

            if (ReferenceEquals(map, lastQueriedMap))
            {
                return lastQueriedIndex;
            }

            BillGroupIndex index = map.GetComponent<BillGroupIndex>();
            lastQueriedMap = map;
            lastQueriedIndex = index;
            return index;
        }

        public void SetDirty()
        {
            dirty = true;
        }

        /// <summary>
        /// Whether this bench shares its bill list with anyone. One hash lookup — this is the
        /// question the hot paths actually ask, and the cheapest form of it.
        /// </summary>
        public bool IsGrouped(Building_WorkTable bench)
        {
            if (bench == null)
            {
                return false;
            }

            EnsureBuilt();
            return groupedBenches.Contains(bench);
        }

        public override void FinalizeInit()
        {
            base.FinalizeInit();
            SetDirty();
        }

        public override void MapComponentTick()
        {
            base.MapComponentTick();

            // The in-flight counts are maintained incrementally on job start and job cleanup;
            // these sweeps are the safety net for a missed hook. Split by cost, because the two
            // failures are not equally bad: an over-count wedges a bill forever and is cheap to
            // detect, while an under-count only degrades to vanilla behaviour and needs a full
            // pawn scan to find.
            int tick = Find.TickManager.TicksGame;

            if (tick % FullReconcileInterval == 0)
            {
                InFlightTracker.ReconcileFully(map);
            }
            else if (tick % PruneInterval == 0)
            {
                InFlightTracker.PruneStoppedWorkers(map);
            }
        }

        /// <summary>Benches following <paramref name="anchor"/>, excluding the anchor itself.</summary>
        public List<Building_WorkTable> MembersOf(Building_WorkTable anchor)
        {
            EnsureBuilt();
            return membersByAnchor.TryGetValue(anchor, out List<Building_WorkTable> members)
                ? members
                : null;
        }

        public bool IsAnchor(Building_WorkTable bench)
        {
            List<Building_WorkTable> members = MembersOf(bench);
            return members != null && members.Count > 0;
        }

        /// <summary>Total benches sharing this bench's bill list, including itself. 1 when ungrouped.</summary>
        public int GroupSize(Building_WorkTable bench)
        {
            Building_WorkTable anchor = AnchorOf(bench);
            if (anchor == null)
            {
                return 1;
            }

            List<Building_WorkTable> members = MembersOf(anchor);
            return 1 + (members?.Count ?? 0);
        }

        /// <summary>
        /// The bench owning the list <paramref name="bench"/> works from, or null if it is in no
        /// group at all. Returns the bench itself when it is the anchor of a real group.
        /// </summary>
        public Building_WorkTable AnchorOf(Building_WorkTable bench)
        {
            CompBillGroup comp = bench?.GetComp<CompBillGroup>();
            if (comp == null)
            {
                return null;
            }

            if (comp.IsMember)
            {
                return comp.Anchor;
            }

            return IsAnchor(bench) ? bench : null;
        }

        /// <summary>
        /// Every bench in the group, anchor first. Empty when ungrouped.
        ///
        /// The returned list is cached and shared — <b>callers must not mutate it</b>. It used to
        /// allocate on every call, and the gizmo code asks once per frame per selected bench
        /// (the group outline and the connecting lines), which made a value that only changes on
        /// link or unlink into steady garbage. Cleared with the rest of the index, so it cannot
        /// outlive the membership it describes.
        /// </summary>
        public List<Building_WorkTable> RosterOf(Building_WorkTable bench)
        {
            Building_WorkTable anchor = AnchorOf(bench);
            if (anchor == null)
            {
                return EmptyRoster;
            }

            if (rosterByAnchor.TryGetValue(anchor, out List<Building_WorkTable> cached))
            {
                return cached;
            }

            List<Building_WorkTable> roster = new List<Building_WorkTable> { anchor };

            List<Building_WorkTable> members = MembersOf(anchor);
            if (members != null)
            {
                roster.AddRange(members);
            }

            rosterByAnchor[anchor] = roster;
            return roster;
        }

        private void EnsureBuilt()
        {
            if (dirty)
            {
                Rebuild();
                dirty = false;
            }
        }

        /// <summary>
        /// Whether every bench in this group can make <paramref name="recipe"/>.
        ///
        /// Always true while linking requires identical recipe sets; kept honest rather than
        /// hardcoded because per-bill linkage (TODO.md) is the change that makes it interesting,
        /// and a lie left here would become a wrong icon then.
        /// </summary>
        public bool AllMembersCanMake(Building_WorkTable anchor, RecipeDef recipe)
        {
            if (anchor == null || recipe == null)
            {
                return true;
            }

            EnsureBuilt();

            if (!commonRecipesByAnchor.TryGetValue(anchor, out HashSet<RecipeDef> common))
            {
                common = BuildCommonRecipes(anchor);
                commonRecipesByAnchor[anchor] = common;
            }

            return common == null || common.Contains(recipe);
        }

        /// <summary>
        /// Intersection of every member's recipe list. Null when the group has no members, which
        /// reads as "no constraint" rather than "nothing allowed".
        /// </summary>
        private HashSet<RecipeDef> BuildCommonRecipes(Building_WorkTable anchor)
        {
            List<Building_WorkTable> roster = RosterOf(anchor);
            if (roster.Count == 0)
            {
                return null;
            }

            HashSet<RecipeDef> common = null;
            foreach (Building_WorkTable member in roster)
            {
                List<RecipeDef> recipes = member.def?.AllRecipes;
                if (recipes == null)
                {
                    return null;
                }

                if (common == null)
                {
                    common = new HashSet<RecipeDef>(recipes);
                }
                else
                {
                    common.IntersectWith(recipes);
                }
            }

            return common;
        }

        private void Rebuild()
        {
            membersByAnchor.Clear();
            commonRecipesByAnchor.Clear();
            rosterByAnchor.Clear();
            groupedBenches.Clear();

            // PotentialBillGiver is defined as "def has recipes", which is the smallest vanilla
            // list that is guaranteed to contain every bench we could have grouped.
            List<Thing> candidates = map.listerThings.ThingsInGroup(ThingRequestGroup.PotentialBillGiver);
            foreach (Thing thing in candidates)
            {
                RegisterIfMember(thing);
            }
        }

        private void RegisterIfMember(Thing thing)
        {
            if (!(thing is Building_WorkTable bench))
            {
                return;
            }

            CompBillGroup comp = bench.GetComp<CompBillGroup>();
            if (comp == null || !comp.IsMember)
            {
                return;
            }

            Building_WorkTable anchor = comp.Anchor;
            if (!membersByAnchor.TryGetValue(anchor, out List<Building_WorkTable> members))
            {
                members = new List<Building_WorkTable>();
                membersByAnchor[anchor] = members;
            }

            members.Add(bench);

            // Both ends of the relationship, so the O(1) test covers anchors too — an anchor is
            // never its own member, so registering only `bench` would leave it looking ungrouped.
            groupedBenches.Add(bench);
            groupedBenches.Add(anchor);
        }
    }
}
