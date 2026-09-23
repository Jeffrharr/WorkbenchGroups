using System;

namespace WorkbenchGroups.Core
{
    /// <summary>
    /// What the urgency sort needs to know about one bill. Plain values, filled in by the adapter
    /// from the live bill and the group's count cache, so the sort itself runs with no game.
    /// </summary>
    public struct UrgencyInput
    {
        /// <summary>This is the group's "do this next" order.</summary>
        public bool IsMarked;

        /// <summary>
        /// Vanilla can count this bill's products (<c>RecipeWorkerCounter.CanCountProducts</c>).
        /// A bill that cannot be counted has no stock to be short of, so it never qualifies for
        /// the "one of each first" tier and never gets a Balance ratio.
        /// </summary>
        public bool Countable;

        /// <summary>
        /// Stock on the map plus what the group's pawns are making right now — see
        /// <see cref="UrgencyOrder.EffectiveCount"/> for why the second half is not optional.
        /// Meaningful only when <see cref="Countable"/>.
        /// </summary>
        public int Count;

        /// <summary>The bill is "do until you have X", so <see cref="Target"/> is its X.</summary>
        public bool HasTarget;

        /// <summary>The "until you have" target. Meaningful only when <see cref="HasTarget"/>.</summary>
        public int Target;

        /// <summary>
        /// Position used to break ties, and the whole of the order when no tier or key separates
        /// two bills. The adapter decides what it means per mode: the authored position when the
        /// player's own order is what should show through, the current one under round robin,
        /// whose rotation *is* its state.
        /// </summary>
        public int BaseIndex;
    }

    /// <summary>
    /// Sorts a group's shared list by how badly each order needs attention.
    ///
    /// Issue #8's finding was that "do this next", "at least one of each first" and "balance by
    /// shortfall" are one mechanism — sort the list by an urgency key — and differ only in the
    /// key. So they are one comparator here, as tiers of one key, rather than three sorts:
    ///
    /// <code>
    ///   1. priority tier   0 for the marked order, else 1
    ///   2. floor tier      0 if countable and short of the floor, else 1
    ///   3. mode key        Balance: count / target for "until you have X" orders,
    ///                      everything else after them. Any other mode: none.
    ///   4. base index      stable tiebreak
    /// </code>
    ///
    /// The floor defaults to zero, which no count is below, so tier 2 is inert until the group
    /// switches "one of each first" on — and because it is a tier rather than a mode, it composes
    /// with every mode, "in order" included.
    ///
    /// Returns a permutation rather than sorting in place, the same shape as
    /// <see cref="CanonicalOrder.Restore"/>: the adapter applies it only if it differs from the
    /// identity, so a sort that changes nothing is not a list mutation anything can observe.
    /// </summary>
    public static class UrgencyOrder
    {
        /// <summary>
        /// The count the sort should see: stock, plus one iteration's output per pawn already
        /// making it.
        ///
        /// Without the in-flight half, "one of each first" fails in exactly the case it exists
        /// for. A pawn starts the shirt order with no shirts in stock; for the whole craft the
        /// stock still reads zero, so the shirt order stays in the "short" tier at the head of the
        /// list and the next idle pawn starts a second shirt. Counting work underway as if it were
        /// made is the same move the overshoot guard makes, for the same reason.
        /// </summary>
        /// <param name="stock">Products on the map, as vanilla counts them for this bill.</param>
        /// <param name="inFlight">Pawns currently working the bill.</param>
        /// <param name="unitsPerIteration">Products one iteration makes. Below one is read as one,
        /// so a recipe that reports nothing still counts its workers.</param>
        public static int EffectiveCount(int stock, int inFlight, int unitsPerIteration)
        {
            long units = unitsPerIteration < 1 ? 1 : unitsPerIteration;
            long claimed = inFlight > 0 ? inFlight : 0;
            long total = (long)(stock > 0 ? stock : 0) + claimed * units;
            return total > int.MaxValue ? int.MaxValue : (int)total;
        }

        /// <summary>Tier 2: whether a bill is still short of the group's floor.</summary>
        public static bool IsBelowFloor(UrgencyInput bill, int floor)
        {
            return bill.Countable && bill.Count < floor;
        }

        /// <summary>
        /// Tier 3 under Balance: the fraction of its target a bill has, lowest first.
        ///
        /// Only "until you have X" orders get a ratio, and vanilla guarantees they are countable —
        /// it refuses that repeat mode on a bill whose products cannot be counted. Everything else
        /// sorts after every ratio, in base order: a "do forever" order has no target to be short
        /// of, and inventing one would be sorting on nothing.
        ///
        /// A target of zero or less is already met by any stock at all, so it sorts after every
        /// real ratio but still ahead of the orders that have no target.
        /// </summary>
        public static double BalanceKey(UrgencyInput bill)
        {
            if (!bill.HasTarget || !bill.Countable)
            {
                return double.PositiveInfinity;
            }

            if (bill.Target <= 0)
            {
                return double.MaxValue;
            }

            return (double)bill.Count / bill.Target;
        }

        /// <summary>
        /// The sorted order, as indices into <paramref name="bills"/>.
        /// </summary>
        /// <param name="bills">One entry per bill, in the list's current order.</param>
        /// <param name="floor">"At least this many of each first". Zero switches the tier off.</param>
        /// <param name="balance">Whether the group is in Balance mode.</param>
        /// <returns>A permutation of <c>0..bills.Length-1</c>: element <c>i</c> is the index of
        /// the bill that belongs at position <c>i</c>.</returns>
        public static int[] Sort(UrgencyInput[] bills, int floor, bool balance)
        {
            UrgencyInput[] input = bills ?? new UrgencyInput[0];
            int[] order = new int[input.Length];
            for (int i = 0; i < order.Length; i++)
            {
                order[i] = i;
            }

            // Array.Sort is not stable, so stability is made explicit: the last key is the base
            // index and the very last is the input position, which no two entries share. The
            // result is then fully determined by the keys, which is what lets the adapter compare
            // it against the identity and skip a no-op.
            Array.Sort(order, (a, b) => Compare(input[a], a, input[b], b, floor, balance));
            return order;
        }

        /// <summary>Whether a permutation leaves every element where it is.</summary>
        public static bool IsIdentity(int[] order)
        {
            if (order == null)
            {
                return true;
            }

            for (int i = 0; i < order.Length; i++)
            {
                if (order[i] != i)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Whether a marked "do until you have X" order has been satisfied — the clearing rule
        /// "do this next" deferred until there was a count cache to read it from.
        ///
        /// Compared against stock alone, not the in-flight figure: the order is finished when the
        /// items exist, and a pawn halfway through the last one has not made it yet. Clearing on
        /// the in-flight count would drop the marker a whole craft early, and if that pawn were
        /// interrupted the order would be left unmarked and short.
        /// </summary>
        /// <param name="stockKnown">False when nothing has counted this bill yet. An unknown count
        /// never clears a marker; the next scan will count it.</param>
        public static bool IsTargetReached(bool stockKnown, int stock, int target)
        {
            return stockKnown && stock >= target;
        }

        private static int Compare(
            UrgencyInput a, int aPos, UrgencyInput b, int bPos, int floor, bool balance)
        {
            int byPriority = PriorityTier(a).CompareTo(PriorityTier(b));
            if (byPriority != 0)
            {
                return byPriority;
            }

            int byFloor = FloorTier(a, floor).CompareTo(FloorTier(b, floor));
            if (byFloor != 0)
            {
                return byFloor;
            }

            if (balance)
            {
                int byRatio = BalanceKey(a).CompareTo(BalanceKey(b));
                if (byRatio != 0)
                {
                    return byRatio;
                }
            }

            int byBase = a.BaseIndex.CompareTo(b.BaseIndex);
            return byBase != 0 ? byBase : aPos.CompareTo(bPos);
        }

        private static int PriorityTier(UrgencyInput bill)
        {
            return bill.IsMarked ? 0 : 1;
        }

        private static int FloorTier(UrgencyInput bill, int floor)
        {
            return IsBelowFloor(bill, floor) ? 0 : 1;
        }
    }
}
