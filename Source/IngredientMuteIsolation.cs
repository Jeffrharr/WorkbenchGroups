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

        public static void LoadInto(BillStack stack, int benchId)
        {
            if (stack == null)
            {
                return;
            }

            foreach (Bill bill in stack.Bills)
            {
                bill.nextTickToSearchForIngredients = Remembered(bill, benchId);
            }
        }

        public static void StoreFrom(BillStack stack, int benchId)
        {
            if (stack == null)
            {
                return;
            }

            foreach (Bill bill in stack.Bills)
            {
                Remember(bill, benchId, bill.nextTickToSearchForIngredients);
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
        }
    }
}
