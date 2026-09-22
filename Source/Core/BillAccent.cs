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
    }
}
