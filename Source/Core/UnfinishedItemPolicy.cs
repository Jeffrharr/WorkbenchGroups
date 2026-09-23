namespace WorkbenchGroups.Core
{
    /// <summary>Which bench a resumed unfinished-item job should be aimed at.</summary>
    public enum ResumeBench
    {
        /// <summary>Vanilla's answer: the bench that owns the list.</summary>
        Owner,

        /// <summary>The group bench the item is already parked at.</summary>
        Parked,

        /// <summary>The bench the work giver is scanning right now.</summary>
        Scanned,
    }

    /// <summary>The two moments round robin could move a bill to the tail.</summary>
    public enum RotationMoment
    {
        JobStart,
        UnitCompleted,
    }

    /// <summary>
    /// The decisions behind unfinished-item orders in a shared list (issue #11), kept free of
    /// game types so they can be tested offline. <c>UnfinishedItemSharing</c> and
    /// <c>RoundRobin</c> are the adapters that feed them.
    /// </summary>
    public static class UnfinishedItemPolicy
    {
        /// <summary>
        /// Where to resume a half-made item.
        ///
        /// The parked bench wins whenever the pawn can use it. That is where the item already
        /// lies, so resuming there costs no carrying, and it means a job that keeps getting
        /// interrupted stays at one bench instead of following whichever bench the pawn happened
        /// to scan first. Only when the parked bench is unusable (busy, forbidden, unpowered,
        /// unreachable) does the job move to the scanned bench. The item is then carried there
        /// and becomes parked at it, so the next resume prefers it. This cannot ping-pong,
        /// because the item only moves when the bench it sits at cannot be used.
        ///
        /// No parked bench (the item is in a stockpile, being carried, or unbound) means there is
        /// nothing to stay at, so the scanned bench is used, as vanilla's plain path would.
        /// If the scanned bench is not in the bill's list either, vanilla's own answer stands.
        /// </summary>
        /// <param name="parkedAtGroupBench">The item rests at a bench of the bill's group.</param>
        /// <param name="parkedUsable">That bench can be used by this pawn right now.</param>
        /// <param name="scannedShares">
        /// The scanned bench works from the bill's list and can make the recipe.
        /// </param>
        public static ResumeBench Choose(bool parkedAtGroupBench, bool parkedUsable, bool scannedShares)
        {
            if (parkedAtGroupBench && parkedUsable)
            {
                return ResumeBench.Parked;
            }

            return scannedShares ? ResumeBench.Scanned : ResumeBench.Owner;
        }

        /// <summary>
        /// Whether round robin should rotate a bill at this moment.
        ///
        /// Plain orders rotate when a job starts. Several pawns scanning at once would otherwise
        /// all take the head bill before it moved ("three of A, then three of B").
        ///
        /// Orders that leave an unfinished item behind rotate when a unit is finished instead.
        /// Rotating them at start sends the order to the tail while its item is half-made. If the
        /// pawn is then interrupted, vanilla's top-down loop starts whatever is now at the head
        /// rather than resuming, and the item waits, bound to its maker, until the order comes
        /// round again. Keeping the order at the head until the unit is done lets the maker
        /// resume it first. The start-time reason matters less here because once the item exists
        /// only its maker may resume it.
        /// </summary>
        public static bool RotatesAt(RotationMoment moment, bool leavesUnfinishedItem)
        {
            return moment == RotationMoment.JobStart ? !leavesUnfinishedItem : leavesUnfinishedItem;
        }
    }
}
