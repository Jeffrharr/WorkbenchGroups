namespace WorkbenchGroups.Core
{
    /// <summary>
    /// Plans the list mutations that implement this mod's ordering features.
    ///
    /// Both features here work the same way, and that is the point: the shared list is really
    /// rearranged, and vanilla's untouched top-down selection then produces the behaviour. Round
    /// robin moves the bill a pawn just started to the tail; "do this next" moves the bill the
    /// player picked to the head. Neither needs a line of selection code of our own.
    /// </summary>
    public static class BillOrdering
    {
        /// <summary>
        /// Works out the remove/insert index pair for rotating one entry to the end of a list,
        /// or returns false when the rotation would be a no-op or is not safe to perform.
        ///
        /// This function is the whole reason we do not call vanilla's <c>BillStack.Reorder</c>.
        /// That method guards only that its computed index is non-negative, so a bill which is
        /// *not* in the stack (index -1 — entirely reachable, since the player can delete a bill
        /// while a pawn is walking to the bench) makes its internal Remove a no-op and its Insert
        /// add a foreign bill to the stack. Refusing index &lt; 0 here is what prevents that.
        /// </summary>
        /// <param name="count">Current length of the list.</param>
        /// <param name="index">Index of the entry to rotate, or -1 if it is not present.</param>
        /// <param name="startedBillIsNextOrder">Whether the bill being rotated is the one the
        /// player marked "do this next". See the remark below for why that cancels the
        /// rotation.</param>
        /// <param name="removeAt">Index to remove from.</param>
        /// <param name="insertAt">Index to insert at, in the list as it stands *after* the
        /// removal. Equals the post-removal length, i.e. an append.</param>
        public static bool TryPlanRotateToTail(
            int count, int index, bool startedBillIsNextOrder, out int removeAt, out int insertAt)
        {
            removeAt = -1;
            insertAt = -1;

            if (startedBillIsNextOrder)
            {
                // The two features would otherwise fight over the same bill once per job start:
                // round robin sends it to the tail the moment a pawn takes it, and the "next
                // order" rule puts it straight back at the head. The list would visibly jump
                // twice per craft while ending up exactly where it began — all cost, no effect.
                //
                // Cancelling the rotation is the right way round rather than cancelling the
                // promotion, because the marker is a deliberate, explicit instruction from the
                // player and the rotation is an automatic background cadence. An explicit
                // instruction outranking an implicit one is what "regardless of ordering mode"
                // means.
                return false;
            }

            if (count < 2)
            {
                // Nothing to rotate against: a single bill is its own round robin.
                return false;
            }

            if (index < 0 || index >= count)
            {
                // Not in the list. See the class remark — this is the corrupting case.
                return false;
            }

            if (index == count - 1)
            {
                // Already last. Skipping the mutation keeps the list still for a group down to
                // one eligible bill, which otherwise churns the UI on every single craft.
                return false;
            }

            removeAt = index;
            insertAt = count - 1;
            return true;
        }

        /// <summary>
        /// The largest batch the row control offers. A cap rather than a free number because the
        /// counter is saved per bill and a typo'd 10000 would silently turn round robin back into
        /// "in order" for that bill; twenty is already more than one pawn makes in a day.
        /// </summary>
        public const int MaxBatchSize = 20;

        /// <summary>
        /// Clamps a stored batch size into the range the cadence understands. Anything below one
        /// — the default of a bill nobody has set, a hand-edited save — means "rotate on every
        /// start", which is plain round robin, so a bad value degrades to the shipped behaviour
        /// rather than to a bill that never rotates.
        /// </summary>
        public static int ClampBatchSize(int batchSize)
        {
            if (batchSize < 1)
            {
                return 1;
            }

            return batchSize > MaxBatchSize ? MaxBatchSize : batchSize;
        }

        /// <summary>
        /// Batched round robin: whether this job start finishes the bill's current batch, and so
        /// should rotate it to the tail.
        ///
        /// Batching is a cadence, not an ordering. Strict round robin pays the walk, the haul and
        /// the ingredient search on every single iteration; "make five, then switch" amortises
        /// all three, and is how players think about a queue anyway. So the only change to round
        /// robin is *how often* the existing rotation fires — the rotation itself, and every
        /// safety rule in <see cref="TryPlanRotateToTail"/>, are untouched.
        ///
        /// A batch of one rotates on every start, which is exactly the round robin that shipped
        /// before batches existed. That is what lets the feature ship inert: every bill defaults
        /// to one.
        ///
        /// Counted in starts rather than completions for the same reason rotation happens at job
        /// start: three pawns scanning together all see the same head bill, and it must be the
        /// third *start* that moves it, or "three at a time" becomes three plus however many
        /// pawns were already on their way.
        /// </summary>
        /// <param name="startsSoFar">Starts of this bill since it last rotated. Negative values,
        /// which nothing should produce, are read as zero.</param>
        /// <param name="batchSize">How many starts make one batch; clamped by
        /// <see cref="ClampBatchSize"/>.</param>
        /// <param name="startsAfter">The counter to store back: zero when the batch completed,
        /// otherwise one more than before.</param>
        /// <returns>True when the bill should rotate now.</returns>
        public static bool CompletesBatch(int startsSoFar, int batchSize, out int startsAfter)
        {
            int batch = ClampBatchSize(batchSize);
            int starts = (startsSoFar < 0 ? 0 : startsSoFar) + 1;

            // ">=" rather than "==" so lowering a bill's batch below its running count — 10 down
            // to 2 while it sits at 5 — rotates on the next start instead of counting up forever
            // towards a number it has already passed.
            if (starts >= batch)
            {
                startsAfter = 0;
                return true;
            }

            startsAfter = starts;
            return false;
        }

        /// <summary>
        /// Works out the remove/insert index pair for moving one entry to the front of a list.
        ///
        /// This is the whole of "do this next". Vanilla walks the bill list top-down, so putting
        /// a bill at the head *is* asking for it first — which is why this feature needs no hook
        /// in <c>WorkGiver_DoBill</c>'s selection loop, the one method this mod's design exists
        /// to stay out of.
        ///
        /// Index bounds are refused for the same reason as the rotation above: the player can
        /// delete the marked bill at any moment, and a plan computed against a bill that is no
        /// longer in the list would move some other bill in its place.
        /// </summary>
        /// <param name="count">Current length of the list.</param>
        /// <param name="index">Index of the entry to promote, or -1 if it is not present.</param>
        /// <param name="removeAt">Index to remove from.</param>
        /// <param name="insertAt">Index to insert at, in the list as it stands after the removal.
        /// Always 0.</param>
        public static bool TryPlanPromoteToHead(int count, int index, out int removeAt, out int insertAt)
        {
            removeAt = -1;
            insertAt = -1;

            if (count < 2)
            {
                // A list of one is already in its own preferred order.
                return false;
            }

            if (index < 0 || index >= count)
            {
                return false;
            }

            if (index == 0)
            {
                // Already at the head. Refusing the no-op matters more than it looks: this plan
                // is re-applied whenever the list is rebuilt (switching ordering mode back to
                // "in order" reprojects the authored order over it), and a remove/insert pair
                // that changes nothing would still count as a list mutation to anything watching.
                return false;
            }

            removeAt = index;
            insertAt = 0;
            return true;
        }

        /// <summary>
        /// Whether a "do this next" marker has been satisfied and should drop off by itself.
        ///
        /// A marker is satisfied when a *counted* order runs out of count. RimWorld has no "this
        /// order is finished" event of its own — a repeat-count bill that reaches zero is not
        /// removed from the list; vanilla simply stops starting it and it sits there at 0 until
        /// the player deletes it — so the remaining count is what "finished" means here.
        ///
        /// <see cref="RepeatModeCode.Forever"/> therefore stays marked until the player unmarks
        /// it, and that is the intended answer rather than a gap. An order that says "do this
        /// forever" has no completion to wait for, so there is no moment at which clearing the
        /// marker would be the right thing to do — and leaving it set is what the player gets in
        /// vanilla anyway, where a forever order stays where they put it until they move it.
        ///
        /// <see cref="RepeatModeCode.TargetCount"/> is not decided here. It finishes when
        /// map-wide stock reaches the target, which only <c>RecipeWorkerCounter.CountProducts</c>
        /// can say — a map-wide walk for any filtered bill, far too expensive for the drawing path
        /// this is consulted on. The stock-aware ordering keeps a per-bill count cache anyway, so
        /// that case is answered from it by <see cref="UrgencyOrder.IsTargetReached"/>; this
        /// method keeps reporting it as not spent, which is what a caller with no count must
        /// assume.
        ///
        /// The mode check is load-bearing rather than defensive. <c>repeatCount</c> is a live
        /// field that keeps whatever value it last held, so a bill that ran its count down to
        /// zero and was then switched to "do forever" still reads zero — without the mode test
        /// it would silently unmark itself the instant it was marked.
        /// </summary>
        /// <param name="mode">The marked bill's repeat mode.</param>
        /// <param name="repeatCount">Remaining iterations, meaningful only under
        /// <see cref="RepeatModeCode.RepeatCount"/>.</param>
        public static bool IsNextOrderSpent(RepeatModeCode mode, int repeatCount)
        {
            return mode == RepeatModeCode.RepeatCount && repeatCount <= 0;
        }
    }
}
