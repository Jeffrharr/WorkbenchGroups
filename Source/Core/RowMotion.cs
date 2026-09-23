namespace WorkbenchGroups.Core
{
    /// <summary>
    /// Nice Bill Tab's own row status (<c>NiceBillTab.TabBillsDrawer.BillStatus</c>), mirrored
    /// member for member so the ordinals line up.
    ///
    /// Mirrored rather than referenced because this mod never references NiceBillTab.dll — the
    /// compat layer receives their value boxed and converts it by ordinal. That makes every
    /// number here load-bearing, which is why NiceBillTabApiTests pins each one against the
    /// shipped assembly: a member inserted above one of these would otherwise silently turn our
    /// rewrite into a different state on screen rather than into any kind of error.
    /// </summary>
    public enum NbtBillStatus
    {
        /// <summary>Their "being worked" state: the only one whose stripes scroll.</summary>
        Processed = 0,

        /// <summary>Queued and would start: static stripes at their 0.2 alpha.</summary>
        Pending = 1,

        /// <summary>Declared by them, never returned by their <c>GetBillStatus</c> and never drawn.</summary>
        Paused = 2,

        /// <summary>
        /// Would not start now (target reached, suspended): nearly invisible stripes, and their row
        /// greys its text. Named as they spell it.
        /// </summary>
        Doned = 3,

        /// <summary>Their red "nobody can do this", repainted grey by us. See <see cref="BillAccentRule.StripeFor"/>.</summary>
        NoOneCanDo = 4,
    }

    /// <summary>
    /// Decides which Nice Bill Tab row plays the moving-stripes animation.
    ///
    /// **What theirs keys on.** Their tab animates exactly one row: the *first* bill in the list
    /// that any free colonist on the map has as <c>CurJob.bill</c>
    /// (<c>TabBillsDrawer.CheckAnyOneDoWork</c>, refreshed on bench change and every 30 ticks).
    /// That asks "is anyone working this", not "is anyone working it *here*". On an ungrouped
    /// bench the two are the same question, because the list belongs to one bench. In a linked
    /// group the list is shared, so their test answers for the whole group: the animation lands
    /// on whichever worked bill sits highest. That can be a bill being made at the *other* bench
    /// while the one being made at this bench stays still, which is the opposite of what the
    /// bench the player is looking at should say.
    ///
    /// **What ours keys on.** The same classification the colours use
    /// (<see cref="BillAccentRule.Classify"/>), so motion and colour can't disagree about a row:
    /// a row scrolls exactly when it is <see cref="BillAccent.WorkedHere"/>. Every row worked
    /// here scrolls, not just the highest, because each of them is true.
    ///
    /// **Why the status and not the scroll offset.** Their <c>DrawBillPreview</c> takes the
    /// status as an argument and derives everything from it: the scroll, the stripe strength
    /// (0.4 moving vs 0.2 still) and whether the text is greyed. Handing it the status we
    /// decided means a row that stops moving also drops to their resting strength and a row
    /// that starts moving gains theirs, all drawn by their own code. Zeroing only the offset
    /// would leave a still row at the "being worked" strength, which is half the old signal left
    /// on the wrong row.
    /// </summary>
    public static class RowMotionRule
    {
        /// <summary>
        /// Whether a row's stripes should scroll.
        ///
        /// Only work at *this* bench. Work at another bench keeps its edge bar and its tooltip, but
        /// a scrolling row is the loudest thing on their tab, and the weaker "somewhere else"
        /// claim must not outshout the bench the player has open. Same reasoning as
        /// <see cref="BillAccentRule.FillsRow"/>, one step further: next up gets a fill but no
        /// motion, because nothing is happening to it yet.
        ///
        /// Blocked never moves, even when a pawn is still on it: <see cref="BillAccentRule.Classify"/>
        /// already lets "nobody can do this" outrank "worked here", and a scrolling grey row would
        /// say both "stuck" and "progressing" at once.
        ///
        /// **A marked row worked here does move, in red.** The marker is not an accent (see
        /// <see cref="BillAccentRule.WashFor"/>): the marker owns the hue, the accent owns the edge
        /// bar, and each keeps its own channel. Motion is a third channel, and it belongs to the
        /// accent, because it describes what the colony is doing rather than what the player asked
        /// for. So the "do this next" order that is also being made here scrolls red: red still
        /// means only "do this next", the scroll says "and it's being made here", and the green
        /// edge bar says the same. Making it green instead would take red off the one row it is for,
        /// and holding it still would hide the fact that work is happening here.
        /// </summary>
        public static bool Animates(BillAccent accent)
        {
            return accent == BillAccent.WorkedHere;
        }

        /// <summary>
        /// The status to hand their row drawer in place of <paramref name="theirs"/>.
        ///
        /// Only the Processed/not-Processed split is ours to move. Anything else is left exactly as
        /// they said:
        /// <list type="bullet">
        /// <item><description>Non-production bills (mech gestation and other <c>Bill_Autonomous</c>)
        /// get Processed from their own gestation state, not from a pawn, and "here" means nothing
        /// for them.</description></item>
        /// <item><description>NoOneCanDo keeps its own row look (grey, by <see cref="BillAccentRule.StripeFor"/>);
        /// it already says the row is not progressing.</description></item>
        /// <item><description>Paused is never produced by them, and we don't invent meanings for it.</description></item>
        /// </list>
        ///
        /// A row we stop from moving becomes what their own <c>GetBillStatus</c> would have called it
        /// had it not been their chosen row: Pending if it would start now, Doned if not. A row we
        /// start moving becomes Processed even when it would not start now, which is their own
        /// precedence too — they test "being worked" before "would start".
        /// </summary>
        /// <param name="wouldStartNow">
        /// <c>Bill.ShouldDoNow()</c>. Only read when <paramref name="theirs"/> is Processed and the row
        /// is not animated here, so the adapter may skip computing it otherwise.
        /// </param>
        public static NbtBillStatus StatusFor(
            NbtBillStatus theirs, bool isProduction, BillAccent accent, bool wouldStartNow)
        {
            if (!isProduction || !IsWorkStatus(theirs))
            {
                return theirs;
            }

            if (Animates(accent))
            {
                return NbtBillStatus.Processed;
            }

            if (theirs != NbtBillStatus.Processed)
            {
                return theirs;
            }

            return wouldStartNow ? NbtBillStatus.Pending : NbtBillStatus.Doned;
        }

        /// <summary>The three statuses their <c>GetBillStatus</c> chooses between for a production bill.</summary>
        private static bool IsWorkStatus(NbtBillStatus status)
        {
            return status == NbtBillStatus.Processed
                || status == NbtBillStatus.Pending
                || status == NbtBillStatus.Doned;
        }
    }
}
