using System.Collections.Generic;

namespace WorkbenchGroups.Core
{
    /// <summary>
    /// Remembers the order the player authored, so switching a group to round robin and back
    /// does not permanently scramble their priorities.
    ///
    /// Round robin is implemented by really rotating the bill list (see <see cref="BillOrdering"/>),
    /// which is what makes vanilla's top-down selection produce round-robin behaviour for free.
    /// The cost is that the player's ordering is destroyed while the mode is on. Snapshotting the
    /// order on the way in and reprojecting it on the way out is what pays that cost back.
    /// </summary>
    public static class CanonicalOrder
    {
        private static readonly string[] None = new string[0];

        /// <summary>
        /// Reprojects the live list onto the remembered order.
        ///
        /// Bills are identified by their unique load ID string rather than by position, because
        /// the two lists are separated by an arbitrary amount of play: bills get added, deleted
        /// and pasted while round robin is running. Anything remembered but no longer present is
        /// dropped; anything present but not remembered is appended in its current relative order,
        /// which puts newly-added bills at the bottom — the same place vanilla puts them.
        /// </summary>
        /// <param name="canonical">Load IDs in the order the player last authored.</param>
        /// <param name="current">Load IDs as the list stands now.</param>
        /// <returns>The current IDs, reordered. Always a permutation of <paramref name="current"/>.</returns>
        public static string[] Restore(string[] canonical, string[] current)
        {
            string[] remembered = canonical ?? None;
            string[] live = current ?? None;

            List<string> unplaced = new List<string>(live);
            List<string> result = new List<string>(live.Length);

            foreach (string id in remembered)
            {
                int at = unplaced.IndexOf(id);
                if (at >= 0)
                {
                    result.Add(unplaced[at]);
                    unplaced.RemoveAt(at);
                }
            }

            // Whatever the snapshot never knew about keeps its current relative order.
            result.AddRange(unplaced);
            return result.ToArray();
        }
    }
}
