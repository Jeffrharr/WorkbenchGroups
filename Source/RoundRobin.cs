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

            // Before rotating, settle whether the list we are about to rotate is still the list we
            // left. Another mod's rearrangement is the player's current intent and has to be
            // absorbed into the snapshot *now*: once our own rotation lands on top of it the two
            // are indistinguishable.
            AbsorbExternalReorder(anchorComp, stack);

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
            //
            // Batched round robin gates all of this on a cadence: a bill with a batch of five
            // stays at the head for five starts and rotates on the fifth. Counted first, so every
            // start is counted whether or not it goes on to rotate; a batch of one — the default —
            // completes on every start, which is the round robin that shipped before batches.
            if (!anchorComp.CountStartAndCheckBatch(bill)
                || !BillOrdering.TryPlanRotateToTail(
                    bills.Count,
                    index,
                    NextOrder.IsNextOrder(anchorComp, bill),
                    out int removeAt,
                    out int insertAt))
            {
                // Still record: a no-op rotation is a perfectly good "this is where we left it",
                // and skipping it would leave the expectation stale enough to read the *next*
                // add or delete as a reorder.
                RecordLastKnownOrder(anchorComp, stack);
                return;
            }

            Bill moved = bills[removeAt];
            bills.RemoveAt(removeAt);
            bills.Insert(insertAt, moved);

            RecordLastKnownOrder(anchorComp, stack);
        }

        /// <summary>
        /// Folds a rearrangement made by something other than this mod into the canonical order.
        ///
        /// <c>Patch_BillStack_Reorder</c> catches the vanilla path eagerly, and this catches
        /// everything else lazily, the next time we look at the list. The lazy check is the one
        /// that matters in practice: Nice Bill Tab's drag-and-drop calls neither
        /// <c>BillStack.Reorder</c> nor anything else patchable, it just mutates
        /// <c>BillStack.Bills</c>, so there is no event to hook and no method of theirs worth
        /// patching — any other mod could do the same thing tomorrow.
        ///
        /// Treating a foreign reorder as the player re-authoring their order is the same judgement
        /// <c>Patch_BillStack_Reorder</c> already makes, and for the same reason: the list they
        /// arranged is the list they were looking at.
        /// </summary>
        private static void AbsorbExternalReorder(CompBillGroup anchorComp, BillStack stack)
        {
            if (anchorComp == null || stack == null)
            {
                return;
            }

            string[] current = LoadIdsOf(stack);

            if (!OrderDivergence.Diverged(anchorComp.LastKnownOrderIds.ToArray(), current))
            {
                return;
            }

            anchorComp.CanonicalOrderIds.Clear();
            anchorComp.CanonicalOrderIds.AddRange(current);

            // The foreign reorder is also the only notice the "do this next" marker gets of it.
            // Vanilla's arrows reach Patch_BillStack_Reorder, which drops a marker the player has
            // dragged something above; a direct list mutation reaches nothing, so without this a
            // marker could keep its red row while sitting mid-list — and the next mode switch
            // would promote it back over the arrangement the player just made.
            NextOrder.ClearIfDisplacedFromHead(anchorComp);
        }

        /// <summary>
        /// Records the list as it now stands, as the baseline the next divergence check compares
        /// against. Every path that deliberately changes the order has to call this, or the change
        /// gets attributed to another mod on the next check.
        /// </summary>
        /// <remarks>
        /// Public because this class is not the only thing that moves bills on purpose: marking an
        /// order "do this next" promotes it to the head, and if that move were left out of the
        /// baseline the next check would read it as another mod's reorder and bake the marker's
        /// position into the player's authored order.
        /// </remarks>
        public static void RecordLastKnownOrder(CompBillGroup anchorComp, BillStack stack)
        {
            if (anchorComp == null || stack == null)
            {
                return;
            }

            anchorComp.LastKnownOrderIds.Clear();
            anchorComp.LastKnownOrderIds.AddRange(LoadIdsOf(stack));

            // Every deliberate move of ours ends here, which makes it the one place to tell a tab
            // that caches the list that it is out of date. Occasionally bumped when nothing moved
            // (a no-op rotation); the cost of that is one unneeded rebuild of their filtered copy.
            OwnBillListMoves.Note();
        }

        private static string[] LoadIdsOf(BillStack stack)
        {
            List<Bill> bills = stack.Bills;
            string[] ids = new string[bills.Count];

            for (int i = 0; i < bills.Count; i++)
            {
                ids[i] = bills[i].GetUniqueLoadID();
            }

            return ids;
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
            anchorComp.CanonicalOrderIds.AddRange(LoadIdsOf(stack));

            // The eager path has just accounted for this reorder, so the lazy check must not
            // account for it again — harmless if it did, but it would mean two mechanisms
            // disagreeing about who handled what.
            RecordLastKnownOrder(anchorComp, stack);
        }

        /// <summary>
        /// Snapshots the player's ordering when a list-rearranging mode is switched on, and puts it
        /// back when the group returns to "in order". Without this, trying a mode out permanently
        /// scrambles their priorities.
        /// </summary>
        public static void SetOrdering(CompBillGroup anchorComp, OrderingMode mode)
        {
            if (anchorComp == null || anchorComp.Ordering == mode)
            {
                return;
            }

            ApplyGroupState(anchorComp, mode, anchorComp.OneEachFirst);
        }

        /// <summary>
        /// Switches the group's "make one of each first" layer on or off. The same kind of switch
        /// as a mode change as far as the authored order is concerned — it rearranges the list in
        /// every mode, "in order" included — so it goes through the same snapshot rule.
        /// </summary>
        public static void SetOneEachFirst(CompBillGroup anchorComp, bool on)
        {
            if (anchorComp == null || anchorComp.OneEachFirst == on)
            {
                return;
            }

            ApplyGroupState(anchorComp, anchorComp.Ordering, on);
        }

        private static void ApplyGroupState(CompBillGroup anchorComp, OrderingMode mode, bool oneEachFirst)
        {
            BillStack stack = anchorComp.Bench?.billStack;

            // Keyed on whether each state *rearranges the list*, not on which mode it is, so a new
            // rearranging mode snapshots on the way in and restores on the way out without this
            // method learning its name. See OrderingTransition for the rule.
            SnapshotAction action = OrderingTransition.Plan(
                anchorComp.RearrangesList,
                OrderingTransition.IsListMutating(mode, oneEachFirst));

            if (action == SnapshotAction.Snapshot)
            {
                anchorComp.CanonicalOrderIds.Clear();
                if (stack != null)
                {
                    anchorComp.CanonicalOrderIds.AddRange(LoadIdsOf(stack));
                    RecordLastKnownOrder(anchorComp, stack);
                }
            }
            else if (action == SnapshotAction.Restore && stack != null)
            {
                // Last chance to notice a drag made while the mode was running. Without this the
                // most direct route to the bug — rearrange the list, switch straight back to
                // in-order — is also the one route the rotation path never gets to check, because
                // no pawn has to start a job in between.
                AbsorbExternalReorder(anchorComp, stack);

                RestoreAuthoredOrder(stack, anchorComp.CanonicalOrderIds);
                anchorComp.CanonicalOrderIds.Clear();

                // Reprojecting the authored order rewrites the whole list, which would drop a
                // marked "do this next" order back wherever it was authored while its row stayed
                // highlighted. The marker outranks the snapshot for the same reason it outranks
                // rotation: it is the more recent, more explicit instruction.
                NextOrder.PromoteToHead(anchorComp);

                // After the promotion, not before: both moves are ours, and the baseline has to
                // describe the list as we finally left it or the promotion reads as a foreign
                // reorder at the next check.
                RecordLastKnownOrder(anchorComp, stack);
            }

            anchorComp.Ordering = mode;
            anchorComp.OneEachFirst = oneEachFirst;

            // A stock-aware state sorts before the next bench scan rather than waiting for its
            // clock; a state that does not sort simply ignores the flag.
            anchorComp.SortDirty = true;
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
