using System.Collections.Generic;

namespace WorkbenchGroups.Core
{
    /// <summary>
    /// Notices that something other than this mod has rearranged a shared bill list.
    ///
    /// <see cref="CanonicalOrder"/> only works if the snapshot is kept current, and the snapshot
    /// is refreshed from <c>Patch_BillStack_Reorder</c> — a patch on the one method vanilla
    /// reorders through. That is not the only way a list gets rearranged. Nice Bill Tab implements
    /// its drag-and-drop with a bare <c>Bills.Remove</c>/<c>Bills.Insert</c> pair on the list
    /// itself, so no reorder patch of any kind fires, and the player's new arrangement is invisible
    /// to the snapshot. Under round robin they then switch back to "in order" and watch the list
    /// they just arranged revert to one from minutes earlier.
    ///
    /// Patching the other mod's drag handler would fix that one mod. Comparing the list against
    /// what we last left it as fixes every mod that mutates the list directly, including ones that
    /// do not exist yet, and needs nothing from them — which is why the detection lives here rather
    /// than in the compatibility layer.
    /// </summary>
    public static class OrderDivergence
    {
        private static readonly string[] None = new string[0];

        /// <summary>
        /// Whether <paramref name="current"/> represents a *reordering* of
        /// <paramref name="expected"/>, as opposed to the same order with bills added or removed.
        ///
        /// The distinction is the whole point. Between two checks the list legitimately gains
        /// bills (vanilla appends at the tail; Nice Bill Tab's paste inserts at an arbitrary
        /// index) and loses them (completed, deleted, dereferenced). None of those is the player
        /// re-authoring their priorities, and treating them as one would resnapshot the canonical
        /// order constantly, quietly making the round-robin restore a no-op.
        ///
        /// So both sequences are projected onto the bills they have in common and those
        /// projections compared. Ignoring the newcomers is what makes an insert-at-index read as
        /// an add rather than a reorder; ignoring the departed is what stops a deletion from
        /// looking like everything below it moving up.
        /// </summary>
        /// <param name="expected">Load IDs in the order this mod last left the list.</param>
        /// <param name="current">Load IDs as the list stands now.</param>
        public static bool Diverged(string[] expected, string[] current)
        {
            string[] previous = expected ?? None;
            string[] live = current ?? None;

            HashSet<string> liveIds = new HashSet<string>(live);
            HashSet<string> previousIds = new HashSet<string>(previous);

            // Load IDs are unique within a save, so these two projections are the same set walked
            // in two different orders — equal in length, and equal element-wise exactly when
            // nothing moved relative to anything else.
            List<string> survived = Project(previous, liveIds);
            List<string> recognised = Project(live, previousIds);

            for (int i = 0; i < survived.Count; i++)
            {
                if (survived[i] != recognised[i])
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>The entries of <paramref name="sequence"/> that are in <paramref name="keep"/>, in order.</summary>
        private static List<string> Project(string[] sequence, HashSet<string> keep)
        {
            List<string> projected = new List<string>(sequence.Length);

            foreach (string id in sequence)
            {
                if (keep.Contains(id))
                {
                    projected.Add(id);
                }
            }

            return projected;
        }
    }
}
