namespace WorkbenchGroups.Core
{
    /// <summary>How a bill relates to the group whose list it is sitting in.</summary>
    public enum BillLinkState
    {
        /// <summary>The bench is in no group, so linkage is not a concept here. Draw nothing.</summary>
        NotApplicable,

        /// <summary>Every bench in the group can work this bill.</summary>
        Shared,

        /// <summary>In a group's list, but only some of its benches can work it.</summary>
        Pinned,
    }

    /// <summary>
    /// Decides which chain icon a bill row gets.
    ///
    /// Trivial logic, kept out of the Harmony patch anyway for one reason: the patch draws, and a
    /// drawing method cannot be asserted on. Splitting it means the rule is unit-tested and only
    /// the three lines that put a texture on screen are not.
    /// </summary>
    public static class BillLinkage
    {
        /// <summary>
        /// <paramref name="benchIsGrouped"/> is whether the tab being drawn belongs to a bench in
        /// a group; <paramref name="workableEverywhere"/> is whether every member can make this
        /// bill's recipe.
        ///
        /// Nothing is drawn for an ungrouped bench. A broken chain on every bill of every
        /// workbench in a colony that has never used this mod would be noise standing in for
        /// information — the icon should appear exactly when there is a group for it to describe.
        /// </summary>
        public static BillLinkState StateFor(bool benchIsGrouped, bool workableEverywhere)
        {
            if (!benchIsGrouped)
            {
                return BillLinkState.NotApplicable;
            }

            return workableEverywhere ? BillLinkState.Shared : BillLinkState.Pinned;
        }
    }
}
