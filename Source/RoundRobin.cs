using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;
using WorkbenchGroups.Core;

namespace WorkbenchGroups
{
    /// <summary>
    /// Implements "one of each, in turn" by really rotating the shared bill list.
    ///
    /// Rotating the list rather than intercepting bill selection is what keeps this mod out of
    /// <c>WorkGiver_DoBill</c>'s selection loop — the single most contested method in the bill
    /// system, private, and already rewritten wholesale by at least one popular mod. Vanilla
    /// works the list top-down, so moving a bill to the bottom the moment someone starts it *is*
    /// round robin, with no selection code of our own to keep correct.
    ///
    /// The rotation happens at job start, not on completion. On completion looks right with one
    /// worker and is wrong with several: three pawns scanning together all see the same bill at
    /// the head, all take it, and it only rotates afterwards — "three of A, then three of B".
    /// </summary>
    public static class RoundRobin
    {
        /// <summary>
        /// Called when any pawn starts a job carrying a bill. Cheap and early-exits for the
        /// overwhelmingly common case of an ungrouped bench or an in-order group.
        /// </summary>
        public static void NotifyBillStarted(Bill bill)
        {
            BillStack stack = bill?.billStack;
            if (stack == null || !(stack.billGiver is Building_WorkTable anchor))
            {
                return;
            }

            CompBillGroup anchorComp = RoundRobinStateOf(anchor);
            if (anchorComp == null)
            {
                return;
            }

            List<Bill> bills = stack.Bills;
            int index = bills.IndexOf(bill);

            // The plan is computed in the pure core, which is also what refuses index -1. Vanilla's
            // BillStack.Reorder does not: given a bill that has been deleted mid-craft it would
            // add a foreign bill to the stack rather than doing nothing.
            //
            // The core is also told whether this is the group's "do this next" order, because
            // otherwise the two features undo each other once per job start: rotation sends the
            // marked bill to the tail and the marker puts it straight back at the head, so the
            // list visibly jumps twice per craft to end up exactly where it began.
            if (!BillOrdering.TryPlanRotateToTail(
                    bills.Count,
                    index,
                    NextOrder.IsNextOrder(anchorComp, bill),
                    out int removeAt,
                    out int insertAt))
            {
                return;
            }

            Bill moved = bills[removeAt];
            bills.RemoveAt(removeAt);
            bills.Insert(insertAt, moved);
        }

        /// <summary>
        /// Replaces the remembered ordering with the list as it currently stands.
        ///
        /// Called when the player drags a bill while round robin is running (see
        /// <c>Patch_BillStack_Reorder</c>). The snapshot taken on the way in cannot know about a
        /// drag made afterwards, so without this, switching back to "in order" would discard the
        /// arrangement the player had just made and restore a older one.
        /// </summary>
        public static void ResnapshotCanonicalOrder(CompBillGroup anchorComp)
        {
            BillStack stack = anchorComp?.Bench?.billStack;
            if (stack == null)
            {
                return;
            }

            anchorComp.CanonicalOrderIds.Clear();
            foreach (Bill bill in stack.Bills)
            {
                anchorComp.CanonicalOrderIds.Add(bill.GetUniqueLoadID());
            }
        }

        /// <summary>
        /// Snapshots the player's ordering when round robin is switched on, and puts it back when
        /// switched off. Without this, trying the mode out permanently scrambles their priorities.
        /// </summary>
        public static void SetOrdering(CompBillGroup anchorComp, OrderingMode mode)
        {
            if (anchorComp == null || anchorComp.Ordering == mode)
            {
                return;
            }

            BillStack stack = anchorComp.Bench?.billStack;

            if (mode == OrderingMode.RoundRobin)
            {
                anchorComp.CanonicalOrderIds.Clear();
                if (stack != null)
                {
                    foreach (Bill bill in stack.Bills)
                    {
                        anchorComp.CanonicalOrderIds.Add(bill.GetUniqueLoadID());
                    }
                }
            }
            else if (stack != null)
            {
                RestoreAuthoredOrder(stack, anchorComp.CanonicalOrderIds);
                anchorComp.CanonicalOrderIds.Clear();

                // Reprojecting the authored order rewrites the whole list, which would drop a
                // marked "do this next" order back wherever it was authored while its row stayed
                // highlighted. The marker outranks the snapshot for the same reason it outranks
                // rotation: it is the more recent, more explicit instruction.
                NextOrder.PromoteToHead(anchorComp);
            }

            anchorComp.Ordering = mode;
        }

        private static void RestoreAuthoredOrder(BillStack stack, List<string> canonicalIds)
        {
            List<Bill> bills = stack.Bills;

            Dictionary<string, Bill> byId = new Dictionary<string, Bill>(bills.Count);
            string[] currentIds = new string[bills.Count];
            for (int i = 0; i < bills.Count; i++)
            {
                string id = bills[i].GetUniqueLoadID();
                currentIds[i] = id;
                byId[id] = bills[i];
            }

            string[] restored = CanonicalOrder.Restore(canonicalIds.ToArray(), currentIds);

            // Restore always returns a permutation of what it was given, so rebuilding the list
            // from it cannot drop or invent a bill.
            bills.Clear();
            foreach (string id in restored)
            {
                bills.Add(byId[id]);
            }
        }

        /// <summary>
        /// The group state behind a bench that is really running round robin, or null.
        ///
        /// Returns the comp rather than a bool because the caller needs it anyway to ask whether
        /// the started bill is the group's marked one, and looking it up twice would walk the
        /// bench's comp list twice on a path that runs on every job start in the colony.
        /// </summary>
        private static CompBillGroup RoundRobinStateOf(Building_WorkTable anchor)
        {
            BillGroupIndex index = BillGroupIndex.For(anchor.Map);
            if (index == null || index.GroupSize(anchor) < 2)
            {
                return null;
            }

            CompBillGroup comp = anchor.GetComp<CompBillGroup>();
            return comp != null && comp.Ordering == OrderingMode.RoundRobin ? comp : null;
        }
    }
}
