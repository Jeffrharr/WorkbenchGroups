namespace WorkbenchGroups.Core
{
    /// <summary>
    /// Decides whether one more pawn may start a bill, given how many are already working it.
    ///
    /// This exists because linking benches introduces a problem vanilla does not have: a bill
    /// lives on exactly one bench, so vanilla can never have two pawns start the same bill at
    /// once. Once benches share a list, three idle cooks will all take "make 1 fine meal" and
    /// produce three. Vanilla's own counters only move when a craft *finishes*, which is far too
    /// late to prevent that.
    ///
    /// The fix is to treat work already underway as if it were already produced — the count is
    /// consumed at job start rather than at completion. That is the whole of the "no overshoot"
    /// requirement, and it is pure arithmetic, so it lives here rather than in the Harmony patch.
    /// </summary>
    public static class OvershootPolicy
    {
        /// <summary>
        /// Reimplements <c>Bill_Production.ShouldDoNow</c>'s arithmetic with an in-flight
        /// subtraction, and deliberately without vanilla's side effect of writing back to
        /// <c>paused</c> — a query that mutates state cannot be safely re-evaluated, and we
        /// re-evaluate this one from a Harmony postfix.
        /// </summary>
        /// <param name="mode">The bill's repeat mode.</param>
        /// <param name="repeatCount">Remaining iterations under <see cref="RepeatModeCode.RepeatCount"/>.</param>
        /// <param name="producedCount">Live map-wide product count under <see cref="RepeatModeCode.TargetCount"/>.</param>
        /// <param name="targetCount">The "until you have" target.</param>
        /// <param name="inFlight">Pawns currently working this bill. Never negative in practice;
        /// clamped anyway so a tracking bug degrades to vanilla behaviour rather than deadlocking
        /// the bill.</param>
        /// <param name="paused">Vanilla's pause latch, read only.</param>
        /// <param name="suspended">The player's suspend toggle.</param>
        public static bool MayStartAnother(
            RepeatModeCode mode,
            int repeatCount,
            int producedCount,
            int targetCount,
            int inFlight,
            bool paused,
            bool suspended)
        {
            if (suspended)
            {
                return false;
            }

            int claimed = inFlight > 0 ? inFlight : 0;

            if (mode == RepeatModeCode.Forever)
            {
                // Unbounded by definition: there is no count to overshoot, so never block a
                // second worker. This is the mode most players leave bulk orders on, and
                // blocking here would make linked benches slower than unlinked ones.
                return true;
            }

            if (mode == RepeatModeCode.RepeatCount)
            {
                return repeatCount - claimed > 0;
            }

            // TargetCount. Vanilla pauses at the target and unpauses at a lower watermark; we
            // honour the latch as given and only add the in-flight term.
            if (paused)
            {
                return false;
            }

            return producedCount + claimed < targetCount;
        }
    }
}
