namespace WorkbenchGroups.Core
{
    /// <summary>What a bill row is currently saying about itself, beyond what vanilla says.</summary>
    public enum BillAccent
    {
        /// <summary>Nothing to add; leave the row as its host drew it.</summary>
        None,

        /// <summary>
        /// Nobody can do this work at all — every colonist has the work type at priority zero,
        /// or none can reach it. Red, and it outranks everything else this enum can say.
        /// </summary>
        Blocked,

        /// <summary>A pawn is working this bill at the bench whose tab is open.</summary>
        WorkedHere,

        /// <summary>A pawn is working it, but at another bench in the group.</summary>
        WorkedElsewhere,

        /// <summary>Nobody is on it, and it is the one that would start next.</summary>
        NextUp,
    }

    /// <summary>
    /// Which translucent fill, if any, a row gets. At most one, which is the whole point of the
    /// type: two 13% washes of different hues on one row mix into a third colour that means
    /// neither of them.
    /// </summary>
    public enum RowWash
    {
        None,

        /// <summary>The accent's own colour — green or blue.</summary>
        Accent,

        /// <summary>The "do this next" marker's red.</summary>
        NextOrder,

        /// <summary>
        /// A dark neutral grey for "nobody can do this work". Only a compact host produces it; see
        /// <see cref="BillAccentRule.StripeFor"/> for why that state is not red.
        /// </summary>
        Blocked,
    }

    /// <summary>
    /// Decides which of those a bill row gets.
    ///
    /// Vanilla never needed any of this: a bill belonged to one bench and the list was worked
    /// top-down, so "what is happening now" was answered by the bill at the top. This mod breaks
    /// both halves of that — the list is shared between benches, and round robin rotates it — so
    /// the row has to say what position used to say.
    /// </summary>
    public static class BillAccentRule
    {
        /// <summary>
        /// Precedence is deliberate, because a bill can satisfy several of these at once: one
        /// pawn can be making a "do 10 times" bill while there is still room for the next pawn to
        /// start it.
        ///
        /// Work in progress outranks what is merely scheduled, and work *here* outranks work
        /// elsewhere — nearest first, in both cases, because the question a player has open in
        /// front of a bench is about that bench.
        ///
        /// "Next up" losing to work elsewhere would be the wrong way round: a bill being made at
        /// the other bench and also due to start here next is, to the player standing at this
        /// bench, mostly the second thing.
        /// </summary>
        public static BillAccent Classify(
            bool blocked, bool workedHere, bool workedElsewhere, bool isNextUp)
        {
            // Red first, unconditionally. Every other state here describes work that is happening
            // or about to; "nobody can do this at all" contradicts all of them, and a row that
            // said "starting next" in blue while nobody was able to start it would be worse than
            // no colour at all. It can genuinely co-occur — a bill already in progress stops being
            // doable the moment its work type is set to priority zero.
            if (blocked)
            {
                return BillAccent.Blocked;
            }

            if (workedHere)
            {
                return BillAccent.WorkedHere;
            }

            if (isNextUp)
            {
                return BillAccent.NextUp;
            }

            return workedElsewhere ? BillAccent.WorkedElsewhere : BillAccent.None;
        }

        /// <summary>
        /// Settles the one contested surface on a row: its fill.
        ///
        /// The "do this next" marker is not an accent and is deliberately not a member of
        /// <see cref="BillAccent"/>. It answers "what did the player ask for", while every accent
        /// answers "what is the colony doing", and a bill is routinely both at once — the marked
        /// order sits at the head of the list, so it is usually also the blue "next up" row, and
        /// often the green one being worked. Folding it into the precedence above would make one
        /// of those facts disappear. So each keeps its own channel: the accent owns the left edge
        /// bar, the marker owns the outline and the PRIORITY badge, and neither channel is shared.
        ///
        /// The fill is the one thing both used to draw. A red wash over a blue wash came out a
        /// muddy violet on exactly the row the player most wants to read, so the fill goes to the
        /// marker: it is the explicit instruction, and the accent loses nothing by it because its
        /// edge bar still says the same thing.
        ///
        /// A compact host — Nice Bill Tab — gets no fill *drawn* by us. It tints the row's
        /// background itself, so there we recolour that tint instead of adding one; a wash laid
        /// over it would say the same thing twice. <see cref="StripeFor"/> decides the recolour.
        ///
        /// Work at another bench never gets a fill even in vanilla's tab: it is the weakest of the
        /// claims and should not shout as loudly as the bench the player is standing at.
        /// </summary>
        public static RowWash WashFor(BillAccent accent, bool marked, bool compact)
        {
            if (compact)
            {
                return RowWash.None;
            }

            if (marked)
            {
                return RowWash.NextOrder;
            }

            return FillsRow(accent) ? RowWash.Accent : RowWash.None;
        }

        /// <summary>
        /// Whether an accent is strong enough to colour a row's whole background — our wash in
        /// vanilla's tab, or the recoloured stripes in Nice Bill Tab's, which are the same surface
        /// in two hosts. Only the two claims about *this* bench qualify. Work at another bench
        /// speaks through the edge bar alone: in Nice Bill Tab that is the only thing that tells it
        /// apart from work here, since both would otherwise repaint the stripes the same green.
        /// </summary>
        /// <remarks>See <see cref="StripeFor"/> for how the compact host's tint is decided.</remarks>
        public static bool FillsRow(BillAccent accent)
        {
            return accent == BillAccent.WorkedHere || accent == BillAccent.NextUp;
        }

        /// <summary>
        /// What colour a compact host's row tint — Nice Bill Tab's stripes — is repainted to, or
        /// <see cref="RowWash.None"/> to leave theirs. The same one-fill-per-row rule as
        /// <see cref="WashFor"/>, applied to the surface that host already draws.
        ///
        /// **Red means "do this next", in both tabs.** Vanilla's tab gives the marked row a red
        /// wash, so the marked row's stripes go red here. The first version left the marked row
        /// with only an outline and arrow while a different row carried red stripes — Nice Bill
        /// Tab's own <c>NoOneCanDo</c> — and the player reasonably read the red row as the
        /// priority one. One colour has to have one meaning across the two tabs.
        ///
        /// **So their "nobody can do this" state is the one place we repaint a state of theirs**,
        /// to a dark grey. Red cannot mean both things, and theirs is the one that yields,
        /// because in Nice Bill Tab 1.6 it never fires: the enum member and its red stripe exist,
        /// but <c>GetBillStatus</c> never returns it and nothing calls the validator that would
        /// compute it. Recolouring a state no player can reach today costs nothing, and if a
        /// future release wires it up, it arrives already distinct from the marker.
        ///
        /// Blocked still outranks everything, the marker included, as red did before: every
        /// other signal describes work that is happening or wanted, and "nobody can do it"
        /// contradicts all of them. A marked order that nobody can do keeps its outline and
        /// arrow, so the mark is still visible; it just does not get to paint the row red over a
        /// fact that says the mark will not be honoured.
        /// </summary>
        public static RowWash StripeFor(BillAccent accent, bool marked)
        {
            if (accent == BillAccent.Blocked)
            {
                return RowWash.Blocked;
            }

            if (marked)
            {
                return RowWash.NextOrder;
            }

            return FillsRow(accent) ? RowWash.Accent : RowWash.None;
        }
    }
}
