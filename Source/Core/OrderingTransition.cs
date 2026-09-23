namespace WorkbenchGroups.Core
{
    /// <summary>What switching ordering mode has to do to the remembered authored order.</summary>
    public enum SnapshotAction
    {
        /// <summary>Leave the snapshot as it is.</summary>
        None,

        /// <summary>Remember the list as it stands now: the player's order is about to be lost.</summary>
        Snapshot,

        /// <summary>Put the remembered order back: the mode that was destroying it has ended.</summary>
        Restore,
    }

    /// <summary>
    /// Decides when a group's authored order is snapshotted and when it is put back.
    ///
    /// Every ordering mode except "in order" works by really rearranging the shared list — round
    /// robin rotates it, and any stock-aware mode re-sorts it — because vanilla's untouched
    /// top-down selection then produces the behaviour for free. The price is that the player's
    /// own arrangement is destroyed while such a mode runs. <see cref="CanonicalOrder"/> pays
    /// that back; this decides when it is asked to.
    ///
    /// The rule used to be "snapshot on the way into round robin, restore on the way out", which
    /// is only right while round robin is the one mode that rearranges. With a second one it is
    /// wrong in two ways at once: switching into the new mode would never snapshot, and
    /// switching it back to "in order" would silently keep the computed order forever. So the
    /// question is whether a mode *rearranges the list*, not which mode it is.
    /// </summary>
    public static class OrderingTransition
    {
        /// <summary>
        /// Whether a mode rearranges the shared list on its own.
        ///
        /// Phrased as "anything but in order" rather than as a list of the modes that do, so a
        /// mode value this build does not know — a save from a newer version, loaded after a
        /// downgrade — is treated as rearranging. That keeps the snapshot it may be carrying
        /// instead of throwing it away, which is the recoverable side to fail on.
        /// </summary>
        public static bool IsListMutating(OrderingMode mode)
        {
            return mode != OrderingMode.InOrder;
        }

        /// <summary>
        /// What a switch from <paramref name="from"/> to <paramref name="to"/> does to the snapshot.
        ///
        /// Moving between two rearranging modes deliberately does nothing. The list at that moment
        /// is already the *first* mode's output — a rotation, a sort — so snapshotting it would
        /// bake that computed order in as if the player had authored it, and the original order
        /// the first switch remembered would be lost for good.
        /// </summary>
        public static SnapshotAction Plan(OrderingMode from, OrderingMode to)
        {
            bool wasMutating = IsListMutating(from);
            bool willMutate = IsListMutating(to);

            if (wasMutating == willMutate)
            {
                return SnapshotAction.None;
            }

            return willMutate ? SnapshotAction.Snapshot : SnapshotAction.Restore;
        }
    }
}
