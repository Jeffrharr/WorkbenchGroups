namespace WorkbenchGroups
{
    /// <summary>
    /// How a group works through its shared bill list. Saved by value on the anchor's comp, so
    /// the numbering is part of the save format — append, never reorder.
    /// </summary>
    public enum OrderingMode
    {
        /// <summary>Vanilla: the list is worked strictly top-down.</summary>
        InOrder = 0,

        /// <summary>One iteration of a bill, then the next, wrapping around.</summary>
        RoundRobin = 1,

        /// <summary>
        /// Stock-aware: "do until you have X" orders are worked emptiest-first by the fraction of
        /// their target in stock, re-sorted before each bench scan. Appended at 2 because this
        /// enum is saved by value — inserting it anywhere else would silently turn every saved
        /// round-robin group into something else.
        /// </summary>
        Balance = 2,
    }
}
