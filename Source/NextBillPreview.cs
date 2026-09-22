using System.Linq;
using RimWorld;
using UnityEngine;

namespace WorkbenchGroups
{
    /// <summary>
    /// Which bill a pawn arriving now would actually start.
    ///
    /// Not simply the top of the list: a suspended bill, one short of ingredients, or one this
    /// mod has already handed to as many pawns as it needs is passed over. Asking the same
    /// <c>ShouldDoNow</c> the work giver asks is what makes this the real answer rather than a
    /// plausible-looking guess that disagrees with the colony the moment anything is unusual.
    /// </summary>
    public static class NextBillPreview
    {
        private static int cachedFrame = -1;

        private static BillStack cachedStack;

        private static Bill cachedBill;

        /// <summary>
        /// Cached per frame per stack, because this is asked once per drawn row and the answer is
        /// a property of the list rather than of the row. Without the cache an open bills tab is
        /// quadratic in <c>ShouldDoNow</c> calls — a method this mod patches, and so do others.
        /// </summary>
        public static Bill In(BillStack stack)
        {
            if (stack == null)
            {
                return null;
            }

            if (cachedFrame == Time.frameCount && ReferenceEquals(cachedStack, stack))
            {
                return cachedBill;
            }

            cachedFrame = Time.frameCount;
            cachedStack = stack;
            cachedBill = stack.Bills.FirstOrDefault(candidate => candidate.ShouldDoNow());

            return cachedBill;
        }
    }
}
