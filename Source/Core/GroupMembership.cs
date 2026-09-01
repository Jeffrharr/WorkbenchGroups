namespace WorkbenchGroups.Core
{
    /// <summary>
    /// The small rules governing a group's shape: when it stops being a group, which member owns
    /// the shared list, and whether a proposed link fits inside vanilla's bill cap.
    /// </summary>
    public static class GroupMembership
    {
        /// <summary>
        /// A group of one is just a bench. Dissolving at this point mirrors vanilla's
        /// <c>StorageGroupManager.Notify_MemberRemoved</c> and means the survivor keeps the bills
        /// as its own rather than holding a group that behaves identically but saves extra state.
        /// </summary>
        public static bool ShouldDissolve(int memberCount)
        {
            return memberCount <= 1;
        }

        /// <summary>
        /// Picks the member that owns the shared bill list.
        ///
        /// The choice must be a pure function of the members, not "whoever was first in some
        /// list", because the anchor is re-derived after every load: if it were positional, a
        /// save/load could silently move the bills onto a different bench and the player would
        /// see their orders jump. Lowest thing ID is stable, total, and free.
        /// </summary>
        public static bool TryElectAnchor(int[] candidateThingIds, out int anchorThingId)
        {
            anchorThingId = 0;

            if (candidateThingIds == null || candidateThingIds.Length == 0)
            {
                return false;
            }

            int best = candidateThingIds[0];
            foreach (int id in candidateThingIds)
            {
                if (id < best)
                {
                    best = id;
                }
            }

            anchorThingId = best;
            return true;
        }

        /// <summary>
        /// Whether the bills on the benches being linked all fit in one stack.
        ///
        /// Vanilla caps a stack at 15 and enforces it in the tab, not in <c>AddBill</c>, so
        /// exceeding it does not throw — it just produces a group whose list cannot be edited.
        /// Checking up front lets the gizmo refuse with a number the player can act on, instead
        /// of silently discarding the bills that did not fit.
        /// </summary>
        public static bool CanMerge(int[] billCountsPerBench, int maxCount, out int total)
        {
            total = 0;

            if (billCountsPerBench != null)
            {
                foreach (int count in billCountsPerBench)
                {
                    total += count;
                }
            }

            return total <= maxCount;
        }
    }
}
