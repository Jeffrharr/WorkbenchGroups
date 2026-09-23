namespace WorkbenchGroups.Core
{
    /// <summary>What the "do this next" button above Nice Bill Tab's list would do if pressed.</summary>
    public enum SelectedBillAction
    {
        /// <summary>No bill of this group is selected: nothing to act on.</summary>
        NoSelection,

        /// <summary>Several are: marking is one-per-group, so there is no single right answer.</summary>
        MultipleSelected,

        /// <summary>Exactly one, and it is not the marked order.</summary>
        Mark,

        /// <summary>Exactly one, and it is already the marked order.</summary>
        Unmark,
    }

    /// <summary>
    /// Decides what the Nice Bill Tab "do this next" button acts on.
    ///
    /// That tab has no free, stable spot on its bill rows for a per-row button (see
    /// <c>NiceBillTabCompat</c>), so the button lives in the strip this mod already reserves
    /// above their list and acts on their *selection* instead. Their selection is a list, and
    /// shift-click adds to it, so "which bill does this button mean" has two answers that are
    /// not a bill: none, and several. Both disable the button rather than guess — marking is one
    /// order per group, so acting on "the first selected" would mark a bill the player may not
    /// even associate with the click.
    /// </summary>
    public static class SelectedBillRule
    {
        /// <param name="selectedBillCount">Selected bills that belong to this bench's list.</param>
        /// <param name="selectedIsMarked">
        /// Whether the single selected bill is the group's marked order. Ignored unless exactly
        /// one bill is selected.
        /// </param>
        public static SelectedBillAction Decide(int selectedBillCount, bool selectedIsMarked)
        {
            if (selectedBillCount <= 0)
            {
                return SelectedBillAction.NoSelection;
            }

            if (selectedBillCount > 1)
            {
                return SelectedBillAction.MultipleSelected;
            }

            return selectedIsMarked ? SelectedBillAction.Unmark : SelectedBillAction.Mark;
        }

        /// <summary>Whether pressing the button does anything at all.</summary>
        public static bool IsActionable(SelectedBillAction action)
        {
            return action == SelectedBillAction.Mark || action == SelectedBillAction.Unmark;
        }
    }
}
