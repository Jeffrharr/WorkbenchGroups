using System;
using System.Collections.Generic;

namespace WorkbenchGroups.Core
{
    /// <summary>
    /// The decision half of the <c>FinishUftJob</c> transpiler: where the two-instruction
    /// pattern occurs, and whether the rewrite should go ahead at all.
    ///
    /// Kept generic over the element type so the same code runs over Harmony's
    /// <c>CodeInstruction</c>s in the shipped mod and over Mono.Cecil's <c>Instruction</c>s in
    /// the offline test — which is what lets the Cecil test prove the real vanilla IL has exactly
    /// the number of matches the transpiler demands, using the matcher the transpiler uses rather
    /// than a second copy of it.
    /// </summary>
    public static class IlPairRewrite
    {
        /// <summary>
        /// How many <c>bill.billStack.billGiver</c> reads <c>FinishUftJob</c> makes in RimWorld 1.6:
        /// one for the haul-off, one for the resume job's target. Any other count means the method
        /// changed shape, and a partial rewrite — the pawn sent to one bench while the haul-off
        /// clears another — is worse than none.
        /// </summary>
        public const int ExpectedFinishUftJobMatches = 2;

        /// <summary>
        /// Indices <c>i</c> where <paramref name="isFirst"/> holds at <c>i</c> and
        /// <paramref name="isSecond"/> at <c>i + 1</c>, left to right, non-overlapping.
        ///
        /// Non-overlapping because each match is replaced as a unit: if the pattern could chain
        /// (first and second both true of one element), an overlapping match would rewrite an
        /// instruction that the previous match already consumed.
        /// </summary>
        public static List<int> FindPairs<T>(IList<T> code, Func<T, bool> isFirst, Func<T, bool> isSecond)
        {
            List<int> starts = new List<int>();
            if (code == null)
            {
                return starts;
            }

            int i = 0;
            while (i + 1 < code.Count)
            {
                bool match = isFirst(code[i]) && isSecond(code[i + 1]);
                if (match)
                {
                    starts.Add(i);
                }

                i += match ? 2 : 1;
            }

            return starts;
        }

        /// <summary>
        /// Whether to apply the rewrite: only when the match count is exactly what the method is
        /// known to contain. All-or-nothing, see <see cref="ExpectedFinishUftJobMatches"/>.
        /// </summary>
        public static bool ShouldRewrite(int matchCount, int expected)
        {
            return expected > 0 && matchCount == expected;
        }
    }
}
