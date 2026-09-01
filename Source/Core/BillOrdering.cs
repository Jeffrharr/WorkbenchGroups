namespace WorkbenchGroups.Core
{
    /// <summary>
    /// Plans the single list mutation that implements round-robin: move the bill a pawn just
    /// started to the tail, so the next pawn to scan finds a different bill at the head.
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
        /// <param name="removeAt">Index to remove from.</param>
        /// <param name="insertAt">Index to insert at, in the list as it stands *after* the
        /// removal. Equals the post-removal length, i.e. an append.</param>
        public static bool TryPlanRotateToTail(int count, int index, out int removeAt, out int insertAt)
        {
            removeAt = -1;
            insertAt = -1;

            if (count < 2)
            {
                // Nothing to rotate against: a single bill is its own round robin.
                return false;
            }

            if (index < 0 || index >= count)
            {
                // Not in this list. See the class remark — this is the corrupting case.
                return false;
            }

            if (index == count - 1)
            {
                // Already last. Skipping the mutation keeps the list still when a group is down
                // to one eligible bill, which otherwise churns the UI every single craft.
                return false;
            }

            removeAt = index;
            insertAt = count - 1;
            return true;
        }
    }
}
