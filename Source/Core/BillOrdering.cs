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
        /// Only "do X times" can answer this, and that is an honest limitation rather than an
        /// omission. RimWorld has no "this order is finished" event at all: a repeat-count bill
        /// that reaches zero is *not* removed from the list — vanilla simply stops starting it,
        /// and it sits there at 0 until the player deletes it. So "until the order is completed"
        /// has to be read off the remaining count, and the count is only meaningful in one of
        /// the three repeat modes.
        ///
        /// The other two genuinely have no completion:
        /// - <see cref="RepeatModeCode.Forever"/> never finishes by definition.
        /// - <see cref="RepeatModeCode.TargetCount"/> finishes when map-wide stock reaches the
        ///   target, which can only be learned by calling <c>RecipeWorkerCounter.CountProducts</c>
        ///   — a map-wide walk of every haulable thing for any bill with a quality, hit-point or
        ///   stuff filter. This test is consulted on the bill-drawing path, once per visible row
        ///   per frame, so doing that here would put the mod's most expensive possible call in
        ///   its hottest loop. Marked bills in those modes stay marked until unmarked; the
        ///   tooltip says so.
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
