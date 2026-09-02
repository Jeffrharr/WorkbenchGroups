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

        private bool dirty = true;

        private const int ReconcileInterval = 250;

        public BillGroupIndex(Map map) : base(map)
        {
        }

        public static BillGroupIndex For(Map map)
        {
            return map?.GetComponent<BillGroupIndex>();
        }

        public void SetDirty()
        {
            dirty = true;
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
            // this periodic sweep is the safety net that stops a missed hook from permanently
            // wedging a bill. Cheap enough at four times a second to not be worth conditioning.
            if (Find.TickManager.TicksGame % ReconcileInterval == 0)
            {
                InFlightTracker.Reconcile(map);
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

        /// <summary>Every bench in the group, anchor first. Empty when ungrouped.</summary>
        public List<Building_WorkTable> RosterOf(Building_WorkTable bench)
        {
            List<Building_WorkTable> roster = new List<Building_WorkTable>();

            Building_WorkTable anchor = AnchorOf(bench);
            if (anchor == null)
            {
                return roster;
            }

            roster.Add(anchor);

            List<Building_WorkTable> members = MembersOf(anchor);
            if (members != null)
            {
                roster.AddRange(members);
            }

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
        }
    }
}
