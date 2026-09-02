using System.Collections.Generic;
using RimWorld;
using Verse;

namespace WorkbenchGroups
{
    /// <summary>
    /// Makes the "couldn't find ingredients, don't retry for ~10 seconds" timer per bench instead
    /// of per bill.
    ///
    /// Vanilla stores that timer in <c>Bill.nextTickToSearchForIngredients</c>, which was
    /// unambiguous while a bill belonged to exactly one bench. Once a group shares its bills, a
    /// single badly-placed bench that cannot reach any steel mutes the bill for every other member
    /// too — including one standing next to the steel. Left alone this is the most visible bug the
    /// mod could ship: "the second bench just stands there".
    ///
    /// The fix treats vanilla's field as scratch space. Before a bench is considered, its own
    /// remembered timer is written into the field; afterwards, whatever vanilla left there is
    /// read back out and stored against that bench. No vanilla code has to change, and with the
    /// feature off the field simply behaves as it always did.
    /// </summary>
    public static class IngredientMuteIsolation
    {
        /// <summary>Per bill, per bench (by thing ID), the tick before which not to retry.</summary>
        private static readonly Dictionary<Bill, Dictionary<int, int>> mutes
            = new Dictionary<Bill, Dictionary<int, int>>();

        /// <summary>
        /// The latest tick any remembered mute runs to, across every bill and bench.
        ///
        /// This one integer is what makes the common case free. A mute is only meaningful while it
        /// is in the future — vanilla's test is <c>TicksGame &lt;= nextTickToSearchForIngredients</c>
        /// — so once every remembered mute has expired, the values sitting in the shared field are
        /// all in the past and therefore harmless. Nothing has to be loaded, and nothing has to be
        /// cleaned up: expiry does it for us.
        ///
        /// Colonies spend almost all of their time in that state, because ingredients are usually
        /// available. Before this, every scan of every grouped bench paid two dictionary lookups
        /// per bill on the way in and a lookup plus a write on the way out, to shuffle zeroes
        /// around.
        /// </summary>
        private static int latestMuteTick;

        public static void LoadInto(BillStack stack, int benchId)
        {
            if (stack == null || !AnyMuteOutstanding())
            {
                return;
            }

            foreach (Bill bill in stack.Bills)
            {
                bill.nextTickToSearchForIngredients = Remembered(bill, benchId);
            }
        }

        /// <summary>
        /// Reads back whatever vanilla decided during the scan.
        ///
        /// Cannot be skipped the way <see cref="LoadInto"/> can: a mute vanilla sets *during* this
        /// scan is sitting in the shared field, and leaving it there is the original bug — one
        /// bench's failed ingredient search muting the bill for every other member.
        ///
        /// It can be made nearly free, though. The common case reads one int per bill and compares
        /// it, touching no dictionary at all; only a bill that is actually muted costs a lookup.
        /// </summary>
        public static void StoreFrom(BillStack stack, int benchId)
        {
            if (stack == null)
            {
                return;
            }

            int now = Find.TickManager.TicksGame;
            bool hadOutstanding = AnyMuteOutstanding();

            foreach (Bill bill in stack.Bills)
            {
                int tick = bill.nextTickToSearchForIngredients;

                // A mute in the future is the only thing worth recording. A past value is either
                // one we wrote and time has retired, or a zero nobody set — both are no-ops for
                // vanilla's check, so storing them would be bookkeeping for its own sake.
                if (tick > now)
                {
                    Remember(bill, benchId, tick);
                }
                else if (hadOutstanding)
                {
                    // Only worth clearing when something could actually be remembered; when
                    // nothing was outstanding this branch would be a dictionary miss per bill.
                    Forget(bill, benchId);
                }
            }
        }

        private static bool AnyMuteOutstanding()
        {
            return latestMuteTick > Find.TickManager.TicksGame;
        }

        private static void Forget(Bill bill, int benchId)
        {
            if (mutes.TryGetValue(bill, out Dictionary<int, int> perBench)
                && perBench.Remove(benchId)
                && perBench.Count == 0)
            {
                mutes.Remove(bill);
            }
        }

        public static void Forget(Bill bill)
        {
            if (bill != null)
            {
                mutes.Remove(bill);
            }
        }

        private static int Remembered(Bill bill, int benchId)
        {
            if (mutes.TryGetValue(bill, out Dictionary<int, int> perBench)
                && perBench.TryGetValue(benchId, out int tick))
            {
                return tick;
            }

            return 0;
        }

        private static void Remember(Bill bill, int benchId, int tick)
        {
            if (!mutes.TryGetValue(bill, out Dictionary<int, int> perBench))
            {
                perBench = new Dictionary<int, int>();
                mutes[bill] = perBench;
            }

            perBench[benchId] = tick;

            if (tick > latestMuteTick)
            {
                latestMuteTick = tick;
            }
        }
    }
}
