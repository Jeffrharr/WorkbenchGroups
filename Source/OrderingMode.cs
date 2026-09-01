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
    }
}
