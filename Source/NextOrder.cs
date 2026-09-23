using System.Collections.Generic;
using RimWorld;
using Verse;
using WorkbenchGroups.Core;

namespace WorkbenchGroups
{
    /// <summary>
    /// "Do this next" — the player marks one order in a group's shared list and it jumps to the
    /// front of the queue, whatever ordering mode the group is in.
    ///
    /// <b>What it actually promises, and what it cannot.</b> Being at the head of the list is not
    /// the same as being worked. <c>WorkGiver_DoBill</c> walks the list top-down and skips any
    /// bill that is suspended, paused, has no reachable ingredients, or whose <c>ShouldDoNow</c>
    /// is false — and it will happily take the second bill when the first is skipped. Genuinely
    /// forcing a bill would mean patching that selection loop, which is the one thing this mod's
    /// whole design is arranged to avoid (see DESIGN.md, "Why top-down, and why it is correct").
    ///
    /// So the marker means "first in line among the orders that can actually run", and the
    /// tooltip says exactly that. A player who marks an order with no ingredients and watches
    /// nothing happen needs to be able to find out why without filing a bug.
    ///
    /// <b>Mechanism.</b> The bill is really moved to index 0, the same way round robin really
    /// moves a started bill to the tail. Both are list mutations rather than selection hooks, and
    /// both are event-driven — this one on the click, that one on a job start — so neither costs
    /// anything on the scan path.
    ///
    /// <b>Staleness.</b> Every read goes through <see cref="Resolve"/>, which looks the marked ID
    /// up in the live list and drops it when the lookup fails. That is what makes it impossible
    /// for a deleted, completed, migrated or orphaned bill to leave a permanently highlighted row
    /// or a dangling reference behind: there is no reference, and a marker nothing answers to is
    /// erased the next time anyone asks.
    /// </summary>
    public static class NextOrder
    {
        /// <summary>
        /// The live marked bill for a group, or null — clearing the marker on the way if it no
        /// longer names anything worth marking.
        ///
        /// This is the single place that decides whether a marker is still real, so every caller
        /// gets the same answer and no caller has to remember the rules. It is also the reason
        /// the feature needs no cleanup hook on most of the ways a bill can go away: a bench
        /// destroyed and its group re-anchored, a bill deleted by another mod, a save loaded with
        /// this mod freshly added or freshly removed and re-added — all of them end in the same
        /// lookup miss.
        /// </summary>
        public static Bill Resolve(CompBillGroup anchorComp)
        {
            if (anchorComp?.NextOrderBillId == null)
            {
                return null;
            }

            Building_WorkTable bench = anchorComp.Bench;
            BillStack stack = bench?.billStack;
            if (stack == null)
            {
                return null;
            }

            // A marker is a group concept — the button that sets it is only drawn on a grouped
            // bench, and "first in line for the group" means nothing to a bench working alone.
            // Unlinking the last groupmate therefore drops it rather than leaving a red row on a
            // bench that no longer has a group to be first in.
            //
            // Gated on the bench being spawned so a gravship jump, during which every member is
            // briefly despawned and the group momentarily looks like a group of one, does not
            // erase the marker in transit. The same care membership itself is given.
            if (bench.Spawned)
            {
                BillGroupIndex index = BillGroupIndex.For(bench.Map);
                if (index != null && index.GroupSize(bench) < 2)
                {
                    anchorComp.NextOrderBillId = null;
                    return null;
                }
            }

            Bill bill = Cached(anchorComp, stack) ?? ResolveById(anchorComp, stack);
            if (bill == null)
            {
                // The ID named a bill that is not in the list any more: deleted by the player,
                // deleted by another mod, or left behind when this bench stopped owning the list.
                anchorComp.NextOrderBillId = null;
                return null;
            }

            if (IsSpent(anchorComp, bill))
            {
                anchorComp.NextOrderBillId = null;
                return null;
            }

            return bill;
        }

        /// <summary>
        /// Whether this exact bill is the one marked. The per-row question, so it takes the comp
        /// the caller already had rather than looking one up per row per frame.
        /// </summary>
        public static bool IsNextOrder(CompBillGroup anchorComp, Bill bill)
        {
            return bill != null && ReferenceEquals(Resolve(anchorComp), bill);
        }

        /// <summary>
        /// Marks an order, or un-marks it if it was already the marked one.
        ///
        /// A toggle rather than a one-shot. "I need twenty meals, now" is the case this exists
        /// for, and a marker that evaporated on the first job start would mean re-clicking it
        /// nineteen times. The cost of sticking is that it needs the clearing rules above; the
        /// cost of not sticking would be the feature not doing the thing it is for.
        /// </summary>
        public static void Toggle(CompBillGroup anchorComp, Bill bill)
        {
            if (anchorComp == null || bill == null)
            {
                return;
            }

            if (IsNextOrder(anchorComp, bill))
            {
                Clear(anchorComp);
                return;
            }

            // Assigning replaces whatever was marked before. One per group is the whole design:
            // several prioritised bills would just be a third ordering mode wearing a button.
            anchorComp.NextOrderBillId = bill.GetUniqueLoadID();
            anchorComp.NextOrderBillCache = bill;
            PromoteToHead(anchorComp);
        }

        public static void Clear(CompBillGroup anchorComp)
        {
            if (anchorComp != null)
            {
                anchorComp.NextOrderBillId = null;
            }
        }

        /// <summary>
        /// Drops the marker if it names the bill being deleted.
        ///
        /// <see cref="Resolve"/> would catch this on its own at the next read, and that safety net
        /// is the one that actually guarantees correctness. This exists so the common case never
        /// reaches the net: deleting a bill is routine, and clearing here means the ID stops
        /// naming a dead bill immediately rather than at the next time something happens to look.
        /// </summary>
        public static void ForgetIfMarked(BillStack stack, Bill bill)
        {
            CompBillGroup anchorComp = AnchorCompOf(stack);
            if (anchorComp?.NextOrderBillId == null || bill == null)
            {
                return;
            }

            if (ReferenceEquals(anchorComp.NextOrderBillCache, bill)
                || anchorComp.NextOrderBillId == bill.GetUniqueLoadID())
            {
                anchorComp.NextOrderBillId = null;
            }
        }

        /// <summary>
        /// Drops the marker if the player has moved something above the marked order.
        ///
        /// The marker's entire effect is "this bill sits at the head of the list". Once the
        /// player drags or arrows another bill above it, that is no longer true, and the honest
        /// options are to fight them by promoting it back or to accept that they have overridden
        /// it. Fighting a drag is the worse of the two — it makes the reorder arrows feel broken
        /// on a bill the player may not even associate with the marker — so a manual reorder that
        /// displaces the marked bill cancels the mark, and the highlight disappears in the same
        /// frame as the thing it was describing.
        /// </summary>
        public static void ClearIfDisplacedFromHead(CompBillGroup anchorComp)
        {
            Bill marked = Resolve(anchorComp);
            if (marked == null)
            {
                return;
            }

            List<Bill> bills = anchorComp.Bench?.billStack?.Bills;
            if (bills != null && bills.Count > 0 && !ReferenceEquals(bills[0], marked))
            {
                Clear(anchorComp);
            }
        }

        /// <summary>
        /// Puts the marked order back at the head of the list.
        ///
        /// Called on marking, and again whenever the list is rebuilt underneath the marker —
        /// switching a group out of round robin reprojects the player's authored order over the
        /// whole list, which would otherwise quietly drop the marked bill back into the middle
        /// while the row stayed highlighted.
        /// </summary>
        public static void PromoteToHead(CompBillGroup anchorComp)
        {
            Bill marked = Resolve(anchorComp);
            if (marked == null)
            {
                return;
            }

            List<Bill> bills = anchorComp.Bench?.billStack?.Bills;
            if (bills == null)
            {
                return;
            }

            // Planned in the pure core, which is also what refuses an index of -1. Vanilla's own
            // BillStack.Reorder does not, and given a bill that is not in the list it would insert
            // a foreign bill rather than doing nothing.
            int index = bills.IndexOf(marked);
            if (!BillOrdering.TryPlanPromoteToHead(bills.Count, index, out int removeAt, out int insertAt))
            {
                return;
            }

            Bill moved = bills[removeAt];
            bills.RemoveAt(removeAt);
            bills.Insert(insertAt, moved);

            // Our own move, so it goes into the baseline that OrderDivergence compares against.
            // Otherwise the next round-robin job start would see the list changed behind its back,
            // take the promotion for a foreign drag and write it into the authored order — so
            // un-marking later would leave the bill at the head for good.
            RoundRobin.RecordLastKnownOrder(anchorComp, anchorComp.Bench.billStack);
        }

        /// <summary>The comp holding a shared list's group state, given the list.</summary>
        public static CompBillGroup AnchorCompOf(BillStack stack)
        {
            return (stack?.billGiver as Building_WorkTable)?.GetComp<CompBillGroup>();
        }

        /// <summary>
        /// The cached bill, if it is still in the list. Membership is checked by reference over at
        /// most fifteen entries, which is cheaper than the string this otherwise has to build.
        /// </summary>
        private static Bill Cached(CompBillGroup anchorComp, BillStack stack)
        {
            Bill cached = anchorComp.NextOrderBillCache;
            return cached != null && stack.Bills.Contains(cached) ? cached : null;
        }

        /// <summary>
        /// The cold path: match the saved ID against the list and re-arm the cache. Runs once
        /// after a load, and once more whenever the marked bill leaves the list.
        /// </summary>
        private static Bill ResolveById(CompBillGroup anchorComp, BillStack stack)
        {
            string wanted = anchorComp.NextOrderBillId;
            foreach (Bill bill in stack.Bills)
            {
                if (bill.GetUniqueLoadID() == wanted)
                {
                    anchorComp.NextOrderBillCache = bill;
                    return bill;
                }
            }

            anchorComp.NextOrderBillCache = null;
            return null;
        }

        /// <summary>
        /// Whether the marked order has finished.
        ///
        /// "Do X times" is answered from the live <c>repeatCount</c> — see
        /// <see cref="BillOrdering.IsNextOrderSpent"/>. "Do until you have X" is answered from the
        /// urgency sort's count cache, and only ever *read* from it here: this runs once per
        /// visible row per frame, and a cache miss must never be the thing that triggers a
        /// map-wide product walk. The cache is kept filled for a marked target order by the scan
        /// prefix even in groups that do not sort, so the answer is at most a second old. Until
        /// the first count lands the marker simply stays, which is the safe side.
        /// </summary>
        private static bool IsSpent(CompBillGroup anchorComp, Bill bill)
        {
            if (!(bill is Bill_Production production))
            {
                return false;
            }

            RepeatModeCode mode = BenchEligibility.RepeatModeOf(production);
            if (mode == RepeatModeCode.TargetCount)
            {
                bool known = UrgencySort.TryPeekCount(anchorComp, bill, out int stock);
                return UrgencyOrder.IsTargetReached(known, stock, production.targetCount);
            }

            return BillOrdering.IsNextOrderSpent(mode, production.repeatCount);
        }
    }
}
